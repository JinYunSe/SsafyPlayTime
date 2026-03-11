import re
from pathlib import Path


PREFAB_DIR = Path("client/Assets/_Project/Prefabs/CameraAndCharacter")
PREFABS = [
    "AIgCharacterAndCamera.prefab",
    "FitCharacterAndCamera.prefab",
    "WiseCharacterAndCamera.prefab",
]


DOC_SPLIT_RE = re.compile(r"(?=^--- !u!\d+ &-?\d+\s*$)", re.MULTILINE)
FILE_ID_RE = re.compile(r"^--- !u!(\d+) &(-?\d+)\s*$", re.MULTILINE)
NAME_RE = re.compile(r"^  m_Name: (.*)$", re.MULTILINE)
GAMEOBJECT_REF_RE = re.compile(r"^  m_GameObject: \{fileID: (-?\d+)\}$", re.MULTILINE)
FATHER_RE = re.compile(r"^  m_Father: \{fileID: (-?\d+)\}$", re.MULTILINE)


def split_docs(text: str):
    parts = [part for part in DOC_SPLIT_RE.split(text) if part.strip()]
    docs = []
    header = ""
    for part in parts:
        match = FILE_ID_RE.match(part)
        if not match:
            if docs:
                raise ValueError("Failed to parse prefab document header.")
            header = part
            continue
        docs.append(
            {
                "class_id": match.group(1),
                "file_id": match.group(2),
                "text": part,
            }
        )
    return header, docs


def find_named_gameobject(docs, name):
    for doc in docs:
        if doc["class_id"] != "1":
            continue
        match = NAME_RE.search(doc["text"])
        if match and match.group(1) == name:
            return doc
    raise ValueError(f"GameObject '{name}' not found.")


def find_gameobject_by_pattern(docs, pattern):
    regex = re.compile(pattern)
    for doc in docs:
        if doc["class_id"] != "1":
            continue
        match = NAME_RE.search(doc["text"])
        if match and regex.fullmatch(match.group(1)):
            return doc
    raise ValueError(f"GameObject matching '{pattern}' not found.")


def find_transform_for_gameobject(docs, gameobject_file_id):
    for doc in docs:
        if doc["class_id"] != "4":
            continue
        match = GAMEOBJECT_REF_RE.search(doc["text"])
        if match and match.group(1) == gameobject_file_id:
            return doc
    raise ValueError(f"Transform for GameObject {gameobject_file_id} not found.")


def parse_component_file_ids(gameobject_doc_text):
    lines = gameobject_doc_text.splitlines()
    components = []
    in_components = False
    for line in lines:
        if line == "  m_Component:":
            in_components = True
            continue
        if in_components:
            if line.startswith("  - component: {fileID: "):
                components.append(line.split(": ", 1)[1].rstrip("}").split()[-1])
                continue
            if line.startswith("  m_Layer: "):
                break
    return components


def replace_component_list(gameobject_doc_text, component_file_ids):
    lines = gameobject_doc_text.splitlines()
    start = lines.index("  m_Component:")
    end = start + 1
    while end < len(lines) and lines[end].startswith("  - component: {fileID: "):
        end += 1

    new_lines = lines[: start + 1]
    for file_id in component_file_ids:
        new_lines.append(f"  - component: {{fileID: {file_id}}}")
    new_lines.extend(lines[end:])
    return "\n".join(new_lines) + "\n"


def parse_children(transform_doc_text):
    lines = transform_doc_text.splitlines()
    start = lines.index("  m_Children:")
    children = []
    i = start + 1
    while i < len(lines) and lines[i].startswith("  - {fileID: "):
        children.append(lines[i].split(": ", 1)[1].rstrip("}").split()[-1])
        i += 1
    return children


def replace_children(transform_doc_text, child_file_ids):
    lines = transform_doc_text.splitlines()
    start = lines.index("  m_Children:")
    end = start + 1
    while end < len(lines) and lines[end].startswith("  - {fileID: "):
        end += 1

    new_lines = lines[: start + 1]
    for file_id in child_file_ids:
        new_lines.append(f"  - {{fileID: {file_id}}}")
    new_lines.extend(lines[end:])
    return "\n".join(new_lines) + "\n"


def replace_single_line(text, pattern, replacement):
    updated, count = re.subn(pattern, replacement, text, count=1, flags=re.MULTILINE)
    if count != 1:
        raise ValueError(f"Expected one replacement for pattern: {pattern}")
    return updated


def migrate_prefab(prefab_path: Path):
    original = prefab_path.read_text(encoding="utf-8")
    header, docs = split_docs(original)

    root_name = prefab_path.stem
    root_go = find_named_gameobject(docs, root_name)
    source_go = find_gameobject_by_pattern(docs, r"Character_.*_humanoid")
    root_transform = find_transform_for_gameobject(docs, root_go["file_id"])
    source_transform = find_transform_for_gameobject(docs, source_go["file_id"])

    source_component_ids = parse_component_file_ids(source_go["text"])
    root_component_ids = parse_component_file_ids(root_go["text"])
    source_transform_id = source_transform["file_id"]

    moved_component_ids = [file_id for file_id in source_component_ids if file_id != source_transform_id]
    root_go["text"] = replace_component_list(root_go["text"], root_component_ids + moved_component_ids)

    root_child_ids = parse_children(root_transform["text"])
    source_child_ids = parse_children(source_transform["text"])
    root_child_ids = [
        child_id
        for child_id in root_child_ids
        if child_id != source_transform_id
    ]
    root_transform["text"] = replace_children(root_transform["text"], root_child_ids + source_child_ids)

    docs_by_id = {doc["file_id"]: doc for doc in docs}

    for file_id in moved_component_ids:
        doc = docs_by_id[file_id]
        doc["text"] = replace_single_line(
            doc["text"],
            r"^  m_GameObject: \{fileID: -?\d+\}$",
            f"  m_GameObject: {{fileID: {root_go['file_id']}}}",
        )

    for child_transform_id in source_child_ids:
        child_doc = docs_by_id[child_transform_id]
        child_doc["text"] = replace_single_line(
            child_doc["text"],
            r"^  m_Father: \{fileID: -?\d+\}$",
            f"  m_Father: {{fileID: {root_transform['file_id']}}}",
        )

    filtered_docs = [
        doc
        for doc in docs
        if doc["file_id"] not in {source_go["file_id"], source_transform["file_id"]}
    ]

    migrated = header + "".join(doc["text"] for doc in filtered_docs)
    if migrated == original:
        print(f"[skip] {prefab_path}")
        return

    prefab_path.write_text(migrated, encoding="utf-8", newline="\n")
    print(f"[updated] {prefab_path}")


def main():
    for prefab_name in PREFABS:
        migrate_prefab(PREFAB_DIR / prefab_name)


if __name__ == "__main__":
    main()
