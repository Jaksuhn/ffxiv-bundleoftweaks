using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Tweaks;

public partial class FFXIVCollectExport {
    private sealed class Snapshot {
        public ulong ContentId;
        public Dictionary<string, HashSet<uint>> Ids = [];
        public Dictionary<string, string> Unavailable = [];
    }

    private static unsafe Snapshot CollectUnlockStates() {
        var unlock = IUnlockState.Get();
        var live = new Snapshot { ContentId = IPlayerState.Get().ContentId };

        void Add(string key, Func<IEnumerable<uint>> ids) {
            try {
                live.Ids[key] = [.. ids()];
            }
            catch (Exception ex) {
                IPluginLog.Get().Warning(ex, $"[{nameof(FFXIVCollectExport)}] Failed to read {key}");
                live.Unavailable[key] = $"Failed to read from game";
            }
        }

        Add("bardings", () => BuddyEquip.Where(unlock.IsBuddyEquipUnlocked).Select(r => r.RowId));
        Add("cards", () => TripleTriadCard.Where(unlock.IsTripleTriadCardUnlocked).Select(r => r.RowId));
        Add("fashions", () => Ornament.Where(unlock.IsOrnamentUnlocked).Select(r => r.RowId));
        Add("frames", () => BannerCondition.Where(r => r.RowId > 1 && IsFrameUnlocked(r)).Select(r => r.RowId));
        Add("hairstyles", () => CharaMakeCustomize.Where(r => r.UnlockLink != 0 && unlock.IsCharaMakeCustomizeUnlocked(r)).Select(r => (uint)r.UnlockLink));
        Add("leves", () => Leve.Where(unlock.IsLeveCompleted).Select(r => r.RowId));
        Add("npcs", () => TripleTriadResident.Where(r => UIState.Instance()->IsTripleTriadNpcBeaten(r.RowId)).Select(r => r.RowId));
        Add("occult_records", () => MKDLore.Where(unlock.IsMKDLoreUnlocked).Select(r => r.RowId));
        Add("orchestrions", () => Orchestrion.Where(unlock.IsOrchestrionUnlocked).Select(r => r.RowId));
        Add("spells", () => AozAction.Where(r => r.RowId != 0 && unlock.IsAozActionUnlocked(r)).Select(r => r.RowId));
        Add("survey_records", () => VVDNotebookContents.Where(row => row.RowId != 0 && PlayerState.Instance()->CompletedVVDNotebookContentsBitArray.TryGet((int)row.RowId - 1, out var unlocked) && unlocked).Select(row => row.RowId));

        var xbm = XBMManager.Instance();
        if (xbm != null && xbm->State == XBMManager.DataState.Received)
            Add("beasts", () => GetUnlockedPets(xbm));
        else
            live.Unavailable["beasts"] = "Beastmaster pet list didn't load. Refresh to try again.";

        // TODO: replace above with this when dalamud is less shit
        //Add("beasts", () => XBMPet.Where(unlock.IsXBMPetUnlocked).Select(r => r.RowId));

        if (GlamourLogIPC.Get() is { IsLoaded: true } glog) {
            var armoire = glog.GetArmoireItemIds().ToHashSet();
            Add("armoires", () => Sheets.Cabinet.Where(r => r.Item.RowId != 0 && armoire.Contains(r.Item.RowId)).Select(r => r.RowId + 1));
            Add("outfits", () => MirageStoreSetItem.Where(r => r.RowId != 0 && glog.IsSetComplete(r.RowId)).Select(r => r.RowId));
        }
        else {
            live.Unavailable["armoires"] = "Requires GlamourLog.";
            live.Unavailable["outfits"] = "Requires GlamourLog.";
        }

        // I cannot be arsed to reverse field records and relics will never happen
        live.Unavailable["field_records"] = "Currently not supported.";
        live.Unavailable["relics"] = "There is no good way to figure out if you've done a relic.";
        return live;
    }

    private static unsafe List<uint> GetUnlockedPets(XBMManager* xbm) {
        var ids = new List<uint>();
        var bits = xbm->UnlockedPetsBitArray;
        for (var i = 0; bits.TryGet(i, out var unlocked); i++)
            if (unlocked)
                ids.Add((uint)i + 1);
        return ids;
    }

    private static unsafe bool IsFrameUnlocked(BannerCondition row) {
        var criteria = row.UnlockCriteria1[0].RowId;
        return row.UnlockType1 switch {
            1 => QuestManager.IsQuestComplete(criteria),
            4 => UIState.IsInstanceContentCompleted(criteria),
            9 => PlayerState.Instance()->IsFramersKitUnlocked(criteria),
            _ => false,
        };
    }
}
