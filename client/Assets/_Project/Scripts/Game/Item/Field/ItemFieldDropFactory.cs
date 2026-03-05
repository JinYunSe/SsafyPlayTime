using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 정의를 기반으로 필드 드랍 오브젝트를 생성한다.
    /// </summary>
    public sealed class ItemFieldDropFactory
    {
        private readonly IItemFieldPrefabResolver _prefabResolver;

        public ItemFieldDropFactory(IItemFieldPrefabResolver prefabResolver)
        {
            _prefabResolver = prefabResolver;
        }

        public ItemFieldDrop Create(ItemDefinition definition, Vector3 position, Transform parent = null)
        {
            if (definition == null)
            {
                return null;
            }

            var prefab = _prefabResolver?.Resolve(definition.Master.PrefabPath);
            GameObject instance;
            if (prefab != null)
            {
                instance = Object.Instantiate(prefab, position, Quaternion.identity, parent);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instance.transform.position = position;
                instance.transform.localScale = Vector3.one * 0.45f;
                if (parent != null)
                {
                    instance.transform.SetParent(parent, true);
                }
            }

            instance.name = $"FieldItem_{definition.Master.ItemId}";
            var fieldDrop = instance.GetComponent<ItemFieldDrop>();
            if (fieldDrop == null)
            {
                fieldDrop = instance.AddComponent<ItemFieldDrop>();
            }

            fieldDrop.SetItemId(definition.Master.ItemId);
            EnsureCollider(instance);
            return fieldDrop;
        }

        private static void EnsureCollider(GameObject target)
        {
            if (target.GetComponentInChildren<Collider>() != null)
            {
                return;
            }

            target.AddComponent<SphereCollider>();
        }
    }
}
