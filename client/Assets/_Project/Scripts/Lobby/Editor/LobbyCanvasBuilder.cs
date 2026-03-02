using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SSAFYPlayTime.EditorTools
{
    public static class LobbyCanvasBuilder
    {
        [MenuItem("Tools/Lobby/Build Basic Canvas UI")]
        public static void Build()
        {
            var existing = GameObject.Find("LobbyCanvas");
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            EnsureEventSystem();

            var canvasObject = new GameObject(
                "LobbyCanvas",
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(SSAFYPlayTime.LobbyCanvasUIController));

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasObject.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var nicknamePanel = CreatePanel("NicknamePanel", root);
            var lobbyPanel = CreatePanel("LobbyPanel", root);
            var roomPanel = CreatePanel("RoomPanel", root);
            var createRoomModal = CreatePanel("CreateRoomModal", root);
            var passwordModal = CreatePanel("PasswordModal", root);

            BuildNicknamePanel(nicknamePanel, out var nicknameInput, out var nicknameConfirmButton);
            BuildLobbyPanel(lobbyPanel, out var lobbyHeaderText, out var lobbyStatusText, out var createRoomOpenButton, out var refreshRoomsButton, out var roomListContent, out var roomItemTemplate);
            BuildRoomPanel(roomPanel, out var roomTitleText, out var roomMembersText, out var leaveRoomButton);
            BuildCreateRoomModal(createRoomModal, out var createRoomNameInput, out var createPrivateToggle, out var createPasswordInput, out var createValidationText, out var createConfirmButton, out var createCancelButton);
            BuildPasswordModal(passwordModal, out var joinPasswordInput, out var passwordValidationText, out var passwordJoinButton, out var passwordCancelButton);

            roomItemTemplate.SetActive(false);

            var controller = canvasObject.GetComponent<SSAFYPlayTime.LobbyCanvasUIController>();
            var serialized = new SerializedObject(controller);
            SetRef(serialized, "nicknamePanel", nicknamePanel.gameObject);
            SetRef(serialized, "lobbyPanel", lobbyPanel.gameObject);
            SetRef(serialized, "roomPanel", roomPanel.gameObject);
            SetRef(serialized, "createRoomModal", createRoomModal.gameObject);
            SetRef(serialized, "passwordModal", passwordModal.gameObject);
            SetRef(serialized, "nicknameInput", nicknameInput);
            SetRef(serialized, "nicknameConfirmButton", nicknameConfirmButton);
            SetRef(serialized, "lobbyHeaderText", lobbyHeaderText);
            SetRef(serialized, "lobbyStatusText", lobbyStatusText);
            SetRef(serialized, "createRoomOpenButton", createRoomOpenButton);
            SetRef(serialized, "refreshRoomsButton", refreshRoomsButton);
            SetRef(serialized, "roomListContent", roomListContent);
            SetRef(serialized, "roomItemTemplate", roomItemTemplate);
            SetRef(serialized, "createRoomNameInput", createRoomNameInput);
            SetRef(serialized, "createPrivateToggle", createPrivateToggle);
            SetRef(serialized, "createPasswordInput", createPasswordInput);
            SetRef(serialized, "createValidationText", createValidationText);
            SetRef(serialized, "createConfirmButton", createConfirmButton);
            SetRef(serialized, "createCancelButton", createCancelButton);
            SetRef(serialized, "joinPasswordInput", joinPasswordInput);
            SetRef(serialized, "passwordValidationText", passwordValidationText);
            SetRef(serialized, "passwordJoinButton", passwordJoinButton);
            SetRef(serialized, "passwordCancelButton", passwordCancelButton);
            SetRef(serialized, "roomTitleText", roomTitleText);
            SetRef(serialized, "roomMembersText", roomMembersText);
            SetRef(serialized, "leaveRoomButton", leaveRoomButton);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = canvasObject;
        }

        private static void BuildNicknamePanel(RectTransform panel, out TMP_InputField nicknameInput, out Button confirmButton)
        {
            var title = CreateText("Title", panel, "닉네임 입력", 44);
            SetRect(title.rectTransform, new Vector2(0.5f, 0.7f), new Vector2(600, 80));

            nicknameInput = CreateInput("NicknameInput", panel, "닉네임");
            SetRect(nicknameInput.GetComponent<RectTransform>(), new Vector2(0.5f, 0.52f), new Vector2(520, 60));

            confirmButton = CreateButton("ConfirmButton", panel, "확인");
            SetRect(confirmButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.38f), new Vector2(240, 60));
        }

        private static void BuildLobbyPanel(
            RectTransform panel,
            out TMP_Text header,
            out TMP_Text status,
            out Button createButton,
            out Button refreshButton,
            out Transform roomListContent,
            out GameObject roomItemTemplate)
        {
            header = CreateText("Header", panel, "닉네임:", 30);
            header.alignment = TextAlignmentOptions.Left;
            SetAnchorRect(header.rectTransform, new Vector2(0.05f, 0.92f), new Vector2(0.55f, 0.98f));

            createButton = CreateButton("CreateRoomButton", panel, "방 만들기");
            SetAnchorRect(createButton.GetComponent<RectTransform>(), new Vector2(0.72f, 0.92f), new Vector2(0.85f, 0.98f));

            refreshButton = CreateButton("RefreshButton", panel, "목록 새로고침");
            SetAnchorRect(refreshButton.GetComponent<RectTransform>(), new Vector2(0.86f, 0.92f), new Vector2(0.99f, 0.98f));

            var listContainer = CreatePanel("RoomListContainer", panel);
            SetAnchorRect(listContainer, new Vector2(0.05f, 0.14f), new Vector2(0.95f, 0.88f));
            listContainer.GetComponent<Image>().color = new Color(0.9f, 0.9f, 0.9f, 1f);

            var scroll = BuildScrollView(listContainer, out roomListContent);

            roomItemTemplate = CreateRoomItemTemplate(roomListContent);
            status = CreateText("Status", panel, "목록을 새로고침하세요.", 24);
            SetAnchorRect(status.rectTransform, new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.10f));

            scroll.vertical = true;
            scroll.horizontal = false;
        }

        private static void BuildRoomPanel(RectTransform panel, out TMP_Text roomTitle, out TMP_Text roomMembers, out Button leaveButton)
        {
            roomTitle = CreateText("RoomTitle", panel, "방 정보", 40);
            SetRect(roomTitle.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(900, 80));

            roomMembers = CreateText("RoomMembers", panel, "참여 인원", 28);
            roomMembers.alignment = TextAlignmentOptions.TopLeft;
            SetRect(roomMembers.rectTransform, new Vector2(0.5f, 0.48f), new Vector2(900, 420));

            leaveButton = CreateButton("LeaveRoomButton", panel, "로비로 나가기");
            SetRect(leaveButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.14f), new Vector2(280, 60));
        }

        private static void BuildCreateRoomModal(
            RectTransform panel,
            out TMP_InputField roomNameInput,
            out Toggle privateToggle,
            out TMP_InputField passwordInput,
            out TMP_Text validation,
            out Button confirm,
            out Button cancel)
        {
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var box = CreatePanel("Dialog", panel);
            SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(700, 450));
            box.GetComponent<Image>().color = Color.white;

            var title = CreateText("Title", box, "방 만들기", 36);
            SetRect(title.rectTransform, new Vector2(0.5f, 0.84f), new Vector2(500, 60));
            title.color = Color.black;

            roomNameInput = CreateInput("RoomNameInput", box, "방 이름");
            SetRect(roomNameInput.GetComponent<RectTransform>(), new Vector2(0.5f, 0.66f), new Vector2(520, 56));

            privateToggle = CreateToggle("PrivateToggle", box, "비공개 방");
            SetRect(privateToggle.GetComponent<RectTransform>(), new Vector2(0.35f, 0.52f), new Vector2(220, 40));

            passwordInput = CreateInput("PasswordInput", box, "비밀번호");
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            passwordInput.inputType = TMP_InputField.InputType.Password;
            SetRect(passwordInput.GetComponent<RectTransform>(), new Vector2(0.5f, 0.40f), new Vector2(520, 56));

            validation = CreateText("Validation", box, string.Empty, 20);
            validation.color = Color.red;
            SetRect(validation.rectTransform, new Vector2(0.5f, 0.28f), new Vector2(600, 44));

            confirm = CreateButton("CreateConfirmButton", box, "생성");
            SetRect(confirm.GetComponent<RectTransform>(), new Vector2(0.38f, 0.14f), new Vector2(220, 56));

            cancel = CreateButton("CreateCancelButton", box, "취소");
            SetRect(cancel.GetComponent<RectTransform>(), new Vector2(0.62f, 0.14f), new Vector2(220, 56));
        }

        private static void BuildPasswordModal(
            RectTransform panel,
            out TMP_InputField passwordInput,
            out TMP_Text validation,
            out Button join,
            out Button cancel)
        {
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var box = CreatePanel("Dialog", panel);
            SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(620, 360));
            box.GetComponent<Image>().color = Color.white;

            var title = CreateText("Title", box, "비밀번호 입력", 34);
            SetRect(title.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(500, 60));
            title.color = Color.black;

            passwordInput = CreateInput("JoinPasswordInput", box, "비밀번호");
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            passwordInput.inputType = TMP_InputField.InputType.Password;
            SetRect(passwordInput.GetComponent<RectTransform>(), new Vector2(0.5f, 0.55f), new Vector2(500, 56));

            validation = CreateText("Validation", box, string.Empty, 20);
            validation.color = Color.red;
            SetRect(validation.rectTransform, new Vector2(0.5f, 0.40f), new Vector2(540, 44));

            join = CreateButton("JoinButton", box, "입장");
            SetRect(join.GetComponent<RectTransform>(), new Vector2(0.38f, 0.20f), new Vector2(200, 56));

            cancel = CreateButton("CancelButton", box, "취소");
            SetRect(cancel.GetComponent<RectTransform>(), new Vector2(0.62f, 0.20f), new Vector2(200, 56));
        }

        private static RectTransform CreatePanel(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.85f);
            return rt;
        }

        private static TMP_Text CreateText(string name, Transform parent, string text, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            if (TmpFontFallbackBootstrap.ActiveFallbackFont != null)
            {
                tmp.font = TmpFontFallbackBootstrap.ActiveFallbackFont;
            }
            return tmp;
        }

        private static Button CreateButton(string name, Transform parent, string text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.85f, 0.85f, 0.85f, 1f);

            var label = CreateText("Text", go.transform, text, 24);
            label.color = Color.black;
            SetAnchorRect(label.rectTransform, Vector2.zero, Vector2.one);

            return go.GetComponent<Button>();
        }

        private static TMP_InputField CreateInput(string name, Transform parent, string placeholder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.white;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            SetAnchorRect(viewportRect, Vector2.zero, Vector2.one);
            viewportRect.offsetMin = new Vector2(10, 8);
            viewportRect.offsetMax = new Vector2(-10, -8);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(viewport.transform, false);
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.color = Color.black;
            text.fontSize = 24;
            text.alignment = TextAlignmentOptions.Left;
            if (TmpFontFallbackBootstrap.ActiveFallbackFont != null)
            {
                text.font = TmpFontFallbackBootstrap.ActiveFallbackFont;
            }
            SetAnchorRect(text.rectTransform, Vector2.zero, Vector2.one);

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            placeholderGo.transform.SetParent(viewport.transform, false);
            var placeholderText = placeholderGo.GetComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            placeholderText.fontSize = 22;
            placeholderText.alignment = TextAlignmentOptions.Left;
            if (TmpFontFallbackBootstrap.ActiveFallbackFont != null)
            {
                placeholderText.font = TmpFontFallbackBootstrap.ActiveFallbackFont;
            }
            SetAnchorRect(placeholderText.rectTransform, Vector2.zero, Vector2.one);

            var input = go.GetComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterValidation = TMP_InputField.CharacterValidation.None;
            return input;
        }

        private static Toggle CreateToggle(string name, Transform parent, string labelText)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.5f);
            bgRect.anchorMax = new Vector2(0f, 0.5f);
            bgRect.sizeDelta = new Vector2(24, 24);
            bgRect.anchoredPosition = new Vector2(12, 0);
            bg.GetComponent<Image>().color = Color.white;

            var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(bg.transform, false);
            var checkRect = check.GetComponent<RectTransform>();
            SetAnchorRect(checkRect, Vector2.zero, Vector2.one);
            checkRect.offsetMin = new Vector2(4, 4);
            checkRect.offsetMax = new Vector2(-4, -4);
            check.GetComponent<Image>().color = Color.black;

            var label = CreateText("Label", go.transform, labelText, 22);
            label.alignment = TextAlignmentOptions.Left;
            label.color = Color.black;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(30, 0);
            labelRect.offsetMax = Vector2.zero;

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = bg.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            return toggle;
        }

        private static ScrollRect BuildScrollView(RectTransform parent, out Transform content)
        {
            var scrollGo = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            SetAnchorRect(scrollGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            scrollGo.GetComponent<Image>().color = new Color(0.96f, 0.96f, 0.96f, 1f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            SetAnchorRect(viewportRect, Vector2.zero, Vector2.one);
            viewport.GetComponent<Image>().color = Color.white;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewport.transform, false);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0, 0);

            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;

            content = contentRect;
            return scroll;
        }

        private static GameObject CreateRoomItemTemplate(Transform parent)
        {
            var button = CreateButton("RoomItemTemplate", parent, "공개/입장가능 방 (0)");
            var layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 52;
            return button.gameObject;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorPos, Vector2 size)
        {
            rect.anchorMin = anchorPos;
            rect.anchorMax = anchorPos;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void SetAnchorRect(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRef(SerializedObject so, string propertyName, Object value)
        {
            so.FindProperty(propertyName).objectReferenceValue = value;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
    }
}
