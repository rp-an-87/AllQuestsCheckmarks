using Comfort.Common;
using EFT;
using EFT.Hideout;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AllQuestsCheckmarks.Helpers
{
    /// <summary>
    /// Every remaining hideout upgrade requirement for items (all stages of all areas), cached and rebuilt when something that
    /// changes it happens (area update, upgrade started, skill up, trader loyalty). Whether an upgrade is "later" (not the next
    /// one of its area, or locked by skills / traders / other areas) is part of the data.
    /// </summary>
    internal static class HideoutNeedsData
    {
        public class Need(EAreaType area, int level, int quantity, bool fir, bool later)
        {
            public EAreaType Area = area;
            public int Level = level;
            public int Quantity = quantity;
            public bool Fir = fir;
            public bool Later = later;
        }

        // same shape the tooltip uses: parallel arrays, one entry per upgrade
        public class ItemNeeds
        {
            public int[] Quantity = [];
            public bool[] Fir = [];
            public bool[] Future = [];
            public string[] Area = [];
            public int[] Level = [];
        }

        private static readonly Dictionary<string, List<Need>> _needs = [];
        private static bool _dirty = true;
        private static HideoutRepresentation? _hookedRepresentation;
        private static EFT.Profile? _hookedProfile;

        public static void Invalidate() => _dirty = true;

        /// <summary>Every remaining item requirement, or false while the hideout is not loaded yet.</summary>
        public static bool TryGetAll(out string[] itemId, out int[] quantity, out bool[] fir, out bool[] later)
        {
            List<string> ids = [];
            List<int> counts = [];
            List<bool> firs = [];
            List<bool> laters = [];

            foreach (KeyValuePair<string, List<Need>> entry in Needs())
            {
                foreach (Need need in entry.Value)
                {
                    ids.Add(entry.Key);
                    counts.Add(need.Quantity);
                    firs.Add(need.Fir);
                    laters.Add(need.Later);
                }
            }

            itemId = [.. ids];
            quantity = [.. counts];
            fir = [.. firs];
            later = [.. laters];

            return ids.Count > 0;
        }

        public static bool TryGet(string templateId, out ItemNeeds result)
        {
            result = new ItemNeeds();

            if (!Needs().TryGetValue(templateId, out List<Need> list) || list.Count == 0)
            {
                return false;
            }

            result.Quantity = [.. list.Select(n => n.Quantity)];
            result.Fir = [.. list.Select(n => n.Fir)];
            result.Future = [.. list.Select(n => n.Later)];
            result.Area = [.. list.Select(n => n.Area.LocalizeAreaName())];
            result.Level = [.. list.Select(n => n.Level)];

            return true;
        }

        private static Dictionary<string, List<Need>> Needs()
        {
            Hook();

            if (_dirty)
            {
                _dirty = false;
                Build();
            }

            return _needs;
        }

        // Listens to what changes the data: done lazily, again when the hideout or the profile object is a new one (new session)
        private static void Hook()
        {
            if (!Singleton<HideoutRepresentation>.Instantiated)
            {
                return;
            }

            HideoutRepresentation representation = Singleton<HideoutRepresentation>.Instance;

            if (!ReferenceEquals(representation, _hookedRepresentation))
            {
                _hookedRepresentation = representation;
                representation.OnAreaUpdated += Invalidate;
                _dirty = true;
            }

            EFT.Profile? profile = ClientAppUtils.GetClientApp()?.GetClientBackEndSession()?.Profile;

            if (profile != null && !ReferenceEquals(profile, _hookedProfile))
            {
                _hookedProfile = profile;
                profile.Skills.AnySkillUp._innerEvent += (_, _) => Invalidate();
                profile.OnTraderLoyaltyChanged += _ => Invalidate();
                _dirty = true;
            }
        }

        private static void Build()
        {
            _needs.Clear();

            if (!Singleton<HideoutRepresentation>.Instantiated)
            {
                _dirty = true;
                return;
            }

            List<AreaData> areas = Singleton<HideoutRepresentation>.Instance.AreaDatas;

            if (areas.Count == 0)
            {
                // too early: try again later
                _dirty = true;
                return;
            }

            bool lockedAsLater = Settings.HideoutLockedAsLater!.Value;

            foreach (AreaData area in areas)
            {
                if (!area.Enabled)
                {
                    continue;
                }

                Stage stage = area.CurrentStage;

                while ((stage = area.StageAt(stage.Level + 1)).Level > 0)
                {
                    bool isNextStage = stage.Level == area.CurrentLevel + 1;

                    // an upgrade already in progress is paid for
                    if (isNextStage &&
                        (area.Status == EAreaStatus.Constructing || area.Status == EAreaStatus.Upgrading ||
                            area.Status == EAreaStatus.ReadyToInstallConstruct || area.Status == EAreaStatus.ReadyToInstallUpgrade))
                    {
                        continue;
                    }

                    // locked: a requirement that is not an item (skill, trader loyalty, another area) is not met yet
                    bool locked = lockedAsLater && stage.Requirements.Any(r => r is not ItemRequirement && !r.Fulfilled);

                    foreach (Requirement requirement in stage.Requirements)
                    {
                        if (requirement is not ItemRequirement item)
                        {
                            continue;
                        }

                        if (!_needs.TryGetValue(item.TemplateId, out List<Need> list))
                        {
                            list = [];
                            _needs.Add(item.TemplateId, list);
                        }

                        list.Add(new Need(area.Template.Type, stage.Level, item.IntCount, item.IsSpawnedInSession, locked || !isNextStage));
                    }
                }
            }

            Plugin.LogSource?.LogInfo($"Loaded hideout requirements - {_needs.Count} unique items");
        }
    }
}
