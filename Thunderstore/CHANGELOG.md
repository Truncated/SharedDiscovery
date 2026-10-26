# Changelog

## Unreleased

- Fixed remote discoveries crashing other mods that patch `Player.AddKnownItem` (e.g. Armoire): the item is now passed as a real item instance with its drop prefab set.
- A remote discovery that throws is no longer retried 30 times with a misleading "prefab was not available" warning.
- If another mod throws but Valheim still recorded the item, the discovery now counts as applied.

## 0.1.0

Initial public release.

- Shared item and material discoveries between players.
- Server-authoritative discovery synchronization.
- Live host-to-client and client-to-host synchronization.
- Snapshot synchronization for players joining later.
- Per-world discovery persistence.
- Safe switching between different worlds in the same game session.
- Support for signed Valheim peer IDs and world UIDs.
- Uses Valheim's native discovery flow and notifications.
