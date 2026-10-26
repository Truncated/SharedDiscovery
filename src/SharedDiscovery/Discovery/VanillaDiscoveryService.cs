using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using SharedDiscovery.Config;
using SharedDiscovery.Core.Discovery;
using UnityEngine;

namespace SharedDiscovery.Discovery
{
    internal sealed class VanillaDiscoveryService
    {
        private readonly Queue<string> _pending = new Queue<string>();
        private readonly HashSet<string> _pendingSet = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly Dictionary<string, int> _attempts = new Dictionary<string, int>(System.StringComparer.Ordinal);
        private bool _processing;
        private int _sessionGeneration;

        public static bool IsApplyingSharedDiscovery { get; private set; }

        public static void ResetApplyingSharedDiscoveryGuard()
        {
            IsApplyingSharedDiscovery = false;
        }

        public void ClearPending()
        {
            _pending.Clear();
            _pendingSet.Clear();
            _attempts.Clear();
            _processing = false;
            _sessionGeneration++;
        }

        public void ApplyOrQueue(string itemId)
        {
            if (!ModConfig.Enabled.Value || !DiscoveryValidator.IsValidItemId(itemId))
            {
                return;
            }

            if (PlayerAlreadyKnows(itemId))
            {
                return;
            }

            if (_pendingSet.Add(itemId))
            {
                _pending.Enqueue(itemId);
                Plugin.DebugLog($"Queued remote discovery: {itemId}");
            }

            if (!_processing && Plugin.Instance != null)
            {
                Plugin.Instance.StartCoroutine(ProcessPending(_sessionGeneration));
            }
        }

        public bool PlayerAlreadyKnows(string itemId)
        {
            Player player = Player.m_localPlayer;
            ItemDrop item = FindItemDrop(itemId);
            return player != null && item != null && player.IsMaterialKnown(item.m_itemData.m_shared.m_name);
        }

        public void ApplySnapshot(IEnumerable<string> itemIds)
        {
            foreach (string itemId in itemIds)
            {
                ApplyOrQueue(itemId);
            }
        }

        private IEnumerator ProcessPending(int generation)
        {
            _processing = true;

            while (generation == _sessionGeneration && _pending.Count > 0)
            {
                string itemId = _pending.Dequeue();
                _pendingSet.Remove(itemId);

                Plugin.DebugLog($"Applying pending remote discovery: {itemId}");
                ApplyResult result = TryApply(itemId);
                if (result == ApplyResult.Failed)
                {
                    // Retrying will hit the same exception again, so give up on this item now.
                    _attempts.Remove(itemId);
                    yield return null;
                }
                else if (result == ApplyResult.NotReady)
                {
                    if (!Plugin.Session.CanApplyRemoteDiscoveries || Player.m_localPlayer == null || MessageHud.instance == null)
                    {
                        Plugin.DebugLog($"Deferred remote discovery '{itemId}': final player not ready.");
                    }

                    int attempts = _attempts.TryGetValue(itemId, out int current) ? current + 1 : 1;
                    _attempts[itemId] = attempts;

                    if (attempts >= 30)
                    {
                        Plugin.Log.LogWarning($"Shared discovery item prefab was not available after retries: {itemId}");
                        _attempts.Remove(itemId);
                        continue;
                    }

                    _pending.Enqueue(itemId);
                    _pendingSet.Add(itemId);
                    yield return new WaitForSeconds(1f);
                }
                else
                {
                    _attempts.Remove(itemId);
                    yield return null;
                }
            }

            if (generation == _sessionGeneration)
            {
                _processing = false;
            }
        }

        private enum ApplyResult
        {
            Applied,
            NotReady,
            Failed
        }

        private static ApplyResult TryApply(string itemId)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ObjectDB.instance == null || !Plugin.Session.IsLocalPlayerReady || MessageHud.instance == null)
            {
                return ApplyResult.NotReady;
            }

            ItemDrop item = FindItemDrop(itemId);
            if (item == null)
            {
                return ApplyResult.NotReady;
            }

            string materialName = item.m_itemData.m_shared.m_name;
            if (player.IsMaterialKnown(materialName))
            {
                return ApplyResult.Applied;
            }

            try
            {
                IsApplyingSharedDiscovery = true;
                player.AddKnownItem(CreateItemInstance(item));
                Plugin.DebugLog($"Applied remote discovery through Player.AddKnownItem: {itemId}");
                return ApplyResult.Applied;
            }
            catch (System.Exception ex)
            {
                // Another mod's AddKnownItem patch can throw after vanilla has already
                // recorded the item. If so, the discovery itself worked.
                if (player.IsMaterialKnown(materialName))
                {
                    Plugin.Log.LogWarning($"Another mod failed while handling remote discovery '{itemId}', but the item was recorded as known: {ex}");
                    return ApplyResult.Applied;
                }

                Plugin.Log.LogError($"Could not apply remote discovery '{itemId}'; it will not be retried this session: {ex}");
                return ApplyResult.Failed;
            }
            finally
            {
                IsApplyingSharedDiscovery = false;
            }
        }

        // The prefab's own ItemData is a template with no m_dropPrefab. Items the game
        // passes to AddKnownItem are real instances that have it, and other mods (Armoire)
        // rely on that, so hand over a copy that looks like a real instance.
        private static ItemDrop.ItemData CreateItemInstance(ItemDrop item)
        {
            ItemDrop.ItemData data = item.m_itemData.Clone();
            data.m_dropPrefab = item.gameObject;
            return data;
        }

        private static ItemDrop FindItemDrop(string itemId)
        {
            if (ObjectDB.instance == null)
            {
                return null!;
            }

            GameObject prefab = ObjectDB.instance.GetItemPrefab(itemId);
            if (prefab == null)
            {
                return null!;
            }

            return prefab.GetComponent<ItemDrop>();
        }
    }
}
