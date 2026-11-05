using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Presentation-only result proxies; never registered as collectible/inventory cards.</summary>
    internal static partial class ComboCollectionCards
    {
        private static ConditionalWeakTable<CollectibleCard, CategoryCatalog.Combo> s_proxies = new ConditionalWeakTable<CollectibleCard, CategoryCatalog.Combo>();
        private sealed class ProxyCost { internal long Value; }
        private static ConditionalWeakTable<CollectibleCard, ProxyCost> s_proxyCosts = new ConditionalWeakTable<CollectibleCard, ProxyCost>();
        private static readonly Dictionary<CollectionCardVisual, Binding> s_bindings = new Dictionary<CollectionCardVisual, Binding>();
        private static readonly MethodInfo s_clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo s_leftClick = AccessTools.Field(typeof(CollectionCardVisual), "m_lastClickLeft");
        private static readonly MethodInfo s_rulesText = typeof(Actor).GetMethod("GetPowersTextObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        private sealed class Caption
        {
            public UberText Label;
            public string Text;
            public bool Lookup, Rich, Resize, Grow, Wrap, Hidden;
        }
        private sealed class Binding
        {
            public CategoryCatalog.Combo Combo;
            public Actor Actor;
            public bool SkipArtwork;
            public string Cost;
            public List<List<CollectibleCard>> LockVariants;
            public CollectionDeck LockRoot, LockContext;
            public float NextLockAt;
            public int LockRevision = -1;
            public CollectionCardVisual.LockType? Lock;
            public string LockReason;
            public long OwnedCombos;
            public CollectionCardVisual.LockType? AppliedLock;
            public string AppliedLockReason;
            public readonly List<Caption> Captions = new List<Caption>();
            public readonly Dictionary<GameObject, bool> Hidden = new Dictionary<GameObject, bool>();
        }
        internal static bool Ready = true;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            Ready = false;
            try { InstallCore(harmony); }
            catch (Exception ex) { Ready = false; DebugLog.Exception("Optional combo collection hooks unavailable", ex); }
        }

        private static void InstallCore(HarmonyLib.Harmony harmony)
        {
            MethodInfo mana = typeof(CollectibleCard).GetProperty("ManaCost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            if (mana != null) harmony.Patch(mana, prefix: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(ReadManaCost))));
            MethodInfo nameGetter = typeof(CollectibleCard).GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            if (nameGetter != null) harmony.Patch(nameGetter, prefix: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(ReadName))));
            InitializeDrag();
            InstallPackageNavigation(harmony);
            int binds = 0;
            var capture = new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(CaptureBinding)));
            foreach (MethodInfo method in typeof(CollectionCardVisual).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.IsAbstract || method.ContainsGenericParameters) continue;
                string name = method.Name;
                if (!name.StartsWith("Show", StringComparison.Ordinal) && !name.StartsWith("Set", StringComparison.Ordinal)
                    && !name.StartsWith("Init", StringComparison.Ordinal) && !name.StartsWith("Bind", StringComparison.Ordinal)
                    && !name.StartsWith("Load", StringComparison.Ordinal)) continue;
                bool acceptsCard = false;
                foreach (ParameterInfo parameter in method.GetParameters()) if (parameter.ParameterType == typeof(CollectibleCard)) acceptsCard = true;
                if (!acceptsCard) continue;
                try { harmony.Patch(method, prefix: capture); binds++; DebugLog.Msg("Combo binding: " + method); }
                catch (Exception ex) { DebugLog.Warning("Combo visual binding unavailable: " + method + ": " + ex.Message); }
            }
            foreach (string name in new[] { "OnRelease", "OnRightClick", "OnDrag" })
            {
                MethodInfo method = AccessTools.Method(typeof(CollectionCardVisual), name);
                if (method != null) harmony.Patch(method, prefix: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(HandleInput))) { priority = Priority.First },
                    finalizer: name == "OnDrag" ? new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(EndDragInput))) : null);
            }
            MethodInfo keywordHelp = AccessTools.Method(typeof(TooltipPanelManager), "UpdateKeywordHelpForCollectionManager");
            if (keywordHelp != null)
                harmony.Patch(keywordHelp, prefix: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(PackageKeywordHelp))) { priority = Priority.First });
            foreach (MethodInfo method in typeof(CollectionInputMgr).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (method.Name == "GrabCard")
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(nameof(PreventGrab))));
            int renderScopes = InstallNativeRendering(harmony);
            Ready = binds > 0 || HasCardField(typeof(CollectionCardVisual)) || renderScopes > 0;
            CollectionPageAnchor.CardKey = StableId;
            DebugLog.Msg("Combo collection binding hooks=" + binds + "; render scopes=" + renderScopes + "; ready=" + Ready);
            if (!Ready) LogBindingApi();
        }
        private static void LogBindingApi()
        {
            // Signatures only: discover the native visual/card relationship on
            // game versions that do not expose a direct CollectibleCard binding.
            foreach (Type type in new[] { typeof(CollectionCardVisual), typeof(CollectionCardActors), typeof(CollectibleCard) })
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (FieldInfo field in type.GetFields(flags))
                    DebugLog.Msg("[Combo API] " + type.FullName + " field " + field.FieldType.FullName + " " + field.Name);
                foreach (PropertyInfo property in type.GetProperties(flags))
                    DebugLog.Msg("[Combo API] " + type.FullName + " property " + property.PropertyType.FullName + " " + property.Name);
                foreach (MethodInfo method in type.GetMethods(flags))
                    if (!method.IsSpecialName) DebugLog.Msg("[Combo API] " + type.FullName + " method " + method);
            }
        }
        private static bool HasCardField(Type type)
        {
            for (; type != null; type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (field.FieldType == typeof(CollectibleCard)) return true;
            return false;
        }
        public static string StableId(CollectibleCard card)
            => card != null && s_proxies.TryGetValue(card, out CategoryCatalog.Combo combo) ? "BCM_COMBO_" + combo.Id : card?.CardId;

        internal static long CollectionManaCost(CollectibleCard card)
            => s_proxyCosts.TryGetValue(card, out ProxyCost cost) ? cost.Value : card.ManaCost;

        internal static string CollectionName(CollectibleCard card)
            => s_proxies.TryGetValue(card, out CategoryCatalog.Combo combo) ? combo.Name : card.Name ?? card.CardId;

        public static bool ReadName(CollectibleCard __instance, ref string __result)
        {
            if (!s_proxies.TryGetValue(__instance, out CategoryCatalog.Combo combo)) return true;
            __result = combo.Name; return false;
        }

        public static bool ReadManaCost(CollectibleCard __instance, ref int __result)
        {
            if (!s_proxyCosts.TryGetValue(__instance, out ProxyCost cost)) return true;
            __result = (int)Math.Min(int.MaxValue, cost.Value);
            return false;
        }

        internal static bool CanRefreshInspectedCard()
        {
            if (s_openPackage != null) return false;
            if (Ready && PreferredQualityMode.IsEnabled)
                foreach (CategoryCatalog.Combo entry in CategoryCatalog.CollectionEntries)
                    if (ExtraFilterState.MatchesEntry(entry)) return false;
            return true;
        }

        internal static void InspectedCardOwnershipChanged(string cardId, bool remainsInScope)
        {
            InvalidateComponentIndex();
            if (!remainsInScope) s_componentScopeIds.Remove(cardId);
        }

        // Plan before category filtering; append after collapse so these identity-
        // distinct entries occupy real native page slots instead of overlaying cards.
        internal static Action Prepare<T>(IEnumerable<KeyValuePair<T, List<CollectibleCard>>> buckets)
        {
            if (PackageViewState.IsActive) return PrepareBrowser(buckets);
            var entries = new List<CategoryCatalog.Combo>();
            if (Ready && PreferredQualityMode.IsEnabled)
                foreach (CategoryCatalog.Combo entry in CategoryCatalog.CollectionEntries)
                    if (ExtraFilterState.MatchesEntry(entry)) entries.Add(entry);
            var lists = new List<List<CollectibleCard>>();
            var names = new List<string>();
            var scope = new HashSet<string>(StringComparer.Ordinal);
            var exactScope = new HashSet<string>(StringComparer.Ordinal);
            var carrierOrder = new Dictionary<CollectibleCard, int>();
            var exactCarriers = new Dictionary<string, List<KeyValuePair<int, CollectibleCard>>>(StringComparer.Ordinal);
            var canonicalCarriers = new Dictionary<string, List<KeyValuePair<int, CollectibleCard>>>(StringComparer.Ordinal);
            void Index(Dictionary<string, List<KeyValuePair<int, CollectibleCard>>> index, string id, CollectibleCard card)
            {
                if (!index.TryGetValue(id, out var matches)) index[id] = matches = new List<KeyValuePair<int, CollectibleCard>>();
                matches.Add(new KeyValuePair<int, CollectibleCard>(lists.Count - 1, card));
            }
            CollectionManager manager = CollectionManager.Get();
            CollectionDeck edited = manager != null && manager.IsInEditMode() ? manager.GetEditedDeck() : null;
            HashSet<string> deckClasses = null;
            if (edited != null && typeof(T) == typeof(TAG_CLASS) && Ready && PreferredQualityMode.IsEnabled)
            {
                deckClasses = new HashSet<string>(StringComparer.Ordinal) { "NEUTRAL" };
                var classes = edited.GetClasses();
                if (classes != null) foreach (TAG_CLASS value in classes) deckClasses.Add(value.ToString());
                else deckClasses.Add(edited.GetClass().ToString());
                var tourists = edited.GetTouristClasses();
                if (tourists != null) foreach (TAG_CLASS value in tourists) deckClasses.Add(value.ToString());
            }
            InvalidateComponentIndex(); s_componentScopeIds.Clear();
            foreach (var bucket in buckets)
            {
                if (bucket.Value == null) continue;
                bucket.Value.RemoveAll(card => card != null && s_proxies.TryGetValue(card, out _));
                lists.Add(bucket.Value); names.Add(bucket.Key.ToString());
                if (deckClasses != null && !deckClasses.Contains(bucket.Key.ToString())) bucket.Value.Clear();
                foreach (CollectibleCard card in bucket.Value)
                    if (card != null && KeywordFilterState.Matches(card)
                        && ExtraFilterState.MatchesExcept(card, FilterTrayKind.Categories, ignorePackages: true) && ExpansionFilterState.Matches(card))
                    {
                        s_componentScopeIds.Add(card.CardId);
                        // Keep edition lookups current after inventory changes,
                        // but build carrier indexes only for visible recipes.
                        if (entries.Count == 0) continue;
                        if (!carrierOrder.ContainsKey(card)) carrierOrder.Add(card, carrierOrder.Count);
                        string canonical = CategoryCatalog.NormalizeCardId(card.CardId);
                        scope.Add(canonical); exactScope.Add(card.CardId);
                        Index(exactCarriers, card.CardId, card); Index(canonicalCarriers, canonical, card);
                    }
            }
            if (s_openPackage != null)
            {
                // Native deck transitions already rebuild these results. Close
                // an ineligible folder in that same pass, without a second refresh.
                var saved = CurrentPackageDefinition();
                if (Ready && PreferredQualityMode.IsEnabled && saved != null
                    && ExtraFilterState.MatchesPackage(saved) && PackageAllowedInDeck(saved))
                { s_openPackage = saved; return PreparePackage(lists); }
                ClearPackageNavigation();
            }
            if (entries.Count == 0) return () => { };
            var pending = new List<KeyValuePair<List<CollectibleCard>, CollectibleCard>>();
            foreach (CategoryCatalog.Combo combo in entries)
            {
                if (combo.IsPackage && typeof(T) == typeof(TAG_CLASS) && !string.IsNullOrEmpty(combo.PackageClass)
                    && combo.PackageClass != "NEUTRAL" && (!names.Contains(combo.PackageClass)
                        || deckClasses != null && !deckClasses.Contains(combo.PackageClass))) continue;
                bool inScope = !combo.IsPackage;
                foreach (CategoryCatalog.CardRequirement requirement in combo.Cards)
                {
                    bool present = combo.ExactVersions ? exactScope.Contains(requirement.Id) : scope.Contains(requirement.Id);
                    if (combo.IsPackage) inScope |= present;
                    else inScope &= present;
                }
                if (!inScope) continue;
                List<CollectibleCard> destination = null; CollectibleCard carrier = null;
                var carriers = combo.ExactVersions ? exactCarriers : canonicalCarriers;
                HashSet<int> comboClasses = null;
                if (!combo.IsPackage && typeof(T) == typeof(TAG_CLASS))
                {
                    bool legal = true;
                    foreach (var member in combo.Cards)
                    {
                        if (!carriers.TryGetValue(member.Id, out var matches)) { legal = false; break; }
                        if (edited != null && !ClassAllowedInDeck(matches[0].Value, edited)) { legal = false; break; }
                        var classes = new HashSet<int>(); bool neutral = false;
                        foreach (var match in matches)
                            if (names[match.Key] == "NEUTRAL") neutral = true; else classes.Add(match.Key);
                        if (neutral) continue;
                        if (comboClasses == null) comboClasses = classes; else comboClasses.IntersectWith(classes);
                    }
                    if (!legal) continue;
                }

                int preferredBucket = combo.IsPackage && typeof(T) == typeof(TAG_CLASS) ? names.IndexOf(combo.PackageClass) : -1;
                int bestRank = -1, bestOrder = int.MaxValue;
                foreach (var member in combo.Cards)
                {
                    if (!carriers.TryGetValue(member.Id, out var candidates)) continue;
                    foreach (var candidate in candidates)
                    {
                        if (comboClasses != null && comboClasses.Count > 0 && !comboClasses.Contains(candidate.Key)) continue;
                        int rank = candidate.Key == preferredBucket ? 2 : names[candidate.Key] != "NEUTRAL" ? 1 : 0;
                        int order = carrierOrder[candidate.Value];
                        if (rank < bestRank || rank == bestRank && order >= bestOrder) continue;
                        bestRank = rank; bestOrder = order;
                        carrier = candidate.Value; destination = lists[candidate.Key];
                    }
                }
                if (carrier == null) continue;
                if (!combo.IsPackage && comboClasses != null && comboClasses.Count == 0 && edited != null)
                { int index = names.IndexOf(edited.GetClass().ToString()); if (index >= 0) destination = lists[index]; }
                if (combo.IsPackage && typeof(T) == typeof(TAG_CLASS) && !string.IsNullOrEmpty(combo.PackageClass))
                { int index = names.IndexOf(combo.PackageClass); if (index >= 0) destination = lists[index]; }
                var proxy = (CollectibleCard)s_clone.Invoke(carrier, null);
                s_proxies.Add(proxy, combo); s_proxyCosts.Add(proxy, new ProxyCost { Value = ManaCost(combo) });
                pending.Add(new KeyValuePair<List<CollectibleCard>, CollectibleCard>(destination, proxy));
            }
            return () => {
                foreach (var entry in pending)
                {
                    // Native results are already sorted within each class.
                    // Keep native cards in place and sort combos by mana/name.
                    long cost = CollectionManaCost(entry.Value);
                    string name = CollectionName(entry.Value);
                    int index = entry.Key.FindIndex(card => card != null && (CollectionManaCost(card) > cost
                        || (CollectionManaCost(card) == cost && StringComparer.CurrentCultureIgnoreCase.Compare(CollectionName(card), name) > 0)));
                    entry.Key.Insert(index < 0 ? entry.Key.Count : index, entry.Value);
                }
                if (pending.Count > 0) DebugLog.Msg("Combo collection: inserted " + pending.Count + " category card(s).");
            };
        }
        public static void CaptureBinding(CollectionCardVisual __instance, object[] __args)
        {
            foreach (object arg in __args)
                if (arg is CollectibleCard card)
                {
                    Forget(__instance);
                    if (s_proxies.TryGetValue(card, out CategoryCatalog.Combo combo)) s_bindings[__instance] = new Binding { Combo = combo };
                    return;
                }
        }
        private static Binding Find(CollectionCardVisual visual)
        {
            if (visual == null) return null;
            CollectionCardActors actors = visual.GetCollectionCardActors();
            if (actors != null && s_actorCombos.TryGetValue(actors, out CategoryCatalog.Combo actorCombo))
            {
                if (!s_bindings.TryGetValue(visual, out Binding found) || found.Combo != actorCombo)
                { Forget(visual); s_bindings[visual] = new Binding { Combo = actorCombo }; }
                return s_bindings[visual];
            }
            for (Type type = visual.GetType(); type != null; type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (field.FieldType == typeof(CollectibleCard) && field.GetValue(visual) is CollectibleCard card)
                    {
                        if (!s_proxies.TryGetValue(card, out CategoryCatalog.Combo combo)) { Forget(visual); return null; }
                        if (!s_bindings.TryGetValue(visual, out Binding bound) || bound.Combo != combo)
                        { Forget(visual); s_bindings[visual] = new Binding { Combo = combo }; }
                        return s_bindings[visual];
                    }
            return s_bindings.TryGetValue(visual, out Binding binding) ? binding : null;
        }
        public static bool IsCombo(CollectionCardVisual visual) => Find(visual) != null;
        public static bool PackageKeywordHelp(Actor __1)
        {
            foreach (Binding binding in s_bindings.Values)
                if (binding.Combo.IsPackage && binding.Actor == __1 && __1 != null)
                {
                    // Keep the native hover/sound path, suppress only the
                    // carrier card's keyword panel for folders and Add All.
                    TooltipPanelManager.Get()?.HideKeywordHelp();
                    return false;
                }
            return true;
        }
        public static bool HandleInput(CollectionCardVisual __instance, MethodBase __originalMethod)
        {
            Binding binding = Find(__instance);
            if (binding == null) return true;
            if (__originalMethod.Name == "OnRelease" && SuppressDragRelease(__instance)) return false;
            MethodInfo ignore = typeof(CollectionCardVisual).GetMethod("ShouldIgnoreAllInput", BindingFlags.Instance | BindingFlags.NonPublic);
            bool blocked = !__instance.IsShown() || (ignore != null && (bool)ignore.Invoke(__instance, null))
                || (CollectionInputMgr.Get()?.HasHeldCard() ?? false) || (UniversalInputManager.Get()?.IsDialogActive() ?? false);
            if (__originalMethod.Name == "OnDrag") return !blocked && BeginDragInput(__instance, binding.Combo);
            if (!blocked)
                try
                {
                    bool left = __originalMethod.Name == "OnRelease" && (Input.GetMouseButtonUp(0)
                        || (!Input.GetMouseButtonUp(1) && (s_leftClick == null || (bool)s_leftClick.GetValue(__instance))));
                    if (left && binding.Combo.IsPackage && !binding.Combo.AddAll) OpenPackage(binding.Combo);
                    else if (left && CanAddCombo()) AddToDeck(binding.Combo, requested: __instance.GetActor()?.GetPremium(), animationSource: __instance.GetActor());
                    else if (binding.Combo.IsPackage) ComboPopup.EditPackage(binding.Combo.AddAll ? s_openPackage ?? binding.Combo : binding.Combo);
                    else ComboPopup.Inspect(binding.Combo, __instance.GetActor());
                }
                catch (Exception ex) { DebugLog.Exception("Combo card action unavailable", ex); }
            return false;
        }
        public static bool PreventGrab(object[] __args)
        {
            foreach (object arg in __args) if (arg is CollectionCardVisual visual && IsCombo(visual)) return IsDragPickup(visual);
            return true;
        }
        public static bool Apply(CollectionCardVisual visual)
        {
            try { return ApplyCore(visual); }
            catch (Exception ex)
            {
                RestoreArtwork(visual);
                DebugLog.Exception("Combo card presentation failed", ex);
                return IsCombo(visual);
            }
        }
        private static bool ApplyCore(CollectionCardVisual visual)
        {
            if (visual != null && s_ownedActors.TryGetValue(visual, out OwnedActors pending)
                && visual.GetCollectionCardActors() != pending.Private) return false;
            Binding binding = Find(visual);
            if (binding == null) return false;
            if (binding.SkipArtwork) return true;
            Actor actor = visual.GetActor();
            if (actor == null) return true;
            if (binding.Actor != actor) { RestoreArtwork(visual); binding.Actor = actor; }
            if (binding.Combo.IsPackage)
            {
                // A folder never exposes the carrier card while its recipe/portrait loads.
                foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                if (visual.m_count != null)
                {
                    GameObject badge = visual.m_count.gameObject;
                    if (!binding.Hidden.ContainsKey(badge)) binding.Hidden[badge] = badge.activeSelf;
                    badge.SetActive(false);
                }
                ApplyLock(visual, binding);
                if (!s_packageTiles.TryGetValue(actor, out PackageNativeTile tile))
                {
                    tile = PackageNativeTile.TryCreate(actor, binding.Combo, visual.transform);
                    if (tile != null) s_packageTiles.Add(actor, tile);
                }
                tile?.RefreshCollection(visual);
                Camera camera = CameraUtils.FindProjectionCameraForObject(visual.gameObject);
                if (camera != null)
                {
                    Rect area = PackageNativeTile.CollectionArea(visual, camera);
                    if (!visual.IsShown()) { tile?.Hide(); return true; }
                    tile?.LayoutLock(visual.m_cardLock, binding.LockReason, area);
                    if (binding.Combo.AddAll) PlacePackageBack(area);
                }
                return true;
            }
            void Caption(UberText label, string text, bool wrap = false, bool fit = true, bool visible = false)
            {
                if (label == null) return;
                bool seen = false; foreach (Caption saved in binding.Captions) if (saved.Label == label) seen = true;
                if (!seen) binding.Captions.Add(new Caption { Label = label, Text = label.Text, Lookup = label.GameStringLookup,
                    Rich = label.RichText, Resize = label.ResizeToFit, Grow = label.ResizeToFitAndGrow, Wrap = label.WordWrap, Hidden = label.isHidden() });
                bool reveal = visible && label.isHidden();
                bool changed = !seen || label.GameStringLookup || !label.RichText || label.ResizeToFit != fit
                    || label.ResizeToFitAndGrow || label.WordWrap != wrap || label.Text != text || reveal;
                if (!changed) return;
                // Card components inject <material> tags for native font/shadow
                // passes. Disabling rich text exposes those tags as glyphs.
                label.GameStringLookup = false; label.RichText = true;
                label.ResizeToFit = fit; label.ResizeToFitAndGrow = false; label.WordWrap = wrap;
                label.Text = text;
                if (reveal) label.Show(); else label.UpdateNow(true);
            }
            Caption(actor.GetNameText(), binding.Combo.Name);
            if (binding.Cost == null) binding.Cost = ManaCost(binding.Combo).ToString(CultureInfo.InvariantCulture);
            void Show(GameObject go)
            {
                if (go == null) return; if (!binding.Hidden.ContainsKey(go)) binding.Hidden[go] = go.activeSelf;
                if (!go.activeSelf) go.SetActive(true);
            }
            UberText mana = actor.GetCostText();
            Show(actor.m_manaObject); Show(mana?.gameObject);
            // Mana widgets can have a zero-width fit region: retain the native
            // digit size instead of shrinking the summed cost out of existence.
            Caption(mana, binding.Cost, fit: false, visible: true);
            Caption(s_rulesText?.Invoke(actor, null) as UberText, ComboCardText.Format(binding.Combo.Appearance.CardText), true);
            void Hide(GameObject go)
            {
                if (go == null) return; if (!binding.Hidden.ContainsKey(go)) binding.Hidden[go] = go.activeSelf;
                if (go.activeSelf) go.SetActive(false);
            }
            Hide(visual.m_newCardCallout); Hide(visual.m_favoriteBanner);
            ApplyLock(visual, binding);
            CollectionCardCount count = visual.m_count;
            if (count?.m_normalCountText != null)
            {
                GetLock(binding, out _); // Shares the bounded refresh interval with lock metadata.
                Show(count.gameObject); Show(count.m_normalTab); Show(count.m_normalCountText.gameObject);
                if (binding.OwnedCombos > 9) { Hide(count.m_normalBorder); Show(count.m_normalWideBorder); }
                else { Show(count.m_normalBorder); Hide(count.m_normalWideBorder); }
                Hide(count.m_trialTab); Hide(count.m_trialWideBorder); Hide(count.m_trialAndOwnedBorder);
                Caption(count.m_normalCountText, binding.OwnedCombos > 9 ? "9+" : "x" + binding.OwnedCombos.ToString(CultureInfo.InvariantCulture), fit: false, visible: true);
            }
            else if (count != null) Hide(count.gameObject);
            ComboCardTemplate.ApplyFrame(actor, binding.Combo.Appearance);
            if (binding.Combo.Appearance.AttackVisible) Caption(actor.GetAttackText(), binding.Combo.Appearance.Attack.ToString(CultureInfo.InvariantCulture), fit: false, visible: true);
            if (binding.Combo.Appearance.SecondaryVisible) Caption(ComboCardTemplate.SecondaryText(actor, binding.Combo.Appearance), binding.Combo.Appearance.Health.ToString(CultureInfo.InvariantCulture), fit: false, visible: true);
            return true;
        }
        public static void RestoreArtwork(CollectionCardVisual visual)
        {
            if (visual == null || !s_bindings.TryGetValue(visual, out Binding binding)) return;
            foreach (Caption saved in binding.Captions)
                if (saved.Label != null)
                {
                    saved.Label.GameStringLookup = saved.Lookup; saved.Label.RichText = saved.Rich;
                    saved.Label.ResizeToFit = saved.Resize; saved.Label.ResizeToFitAndGrow = saved.Grow; saved.Label.WordWrap = saved.Wrap;
                    saved.Label.Text = saved.Text;
                    if (saved.Hidden) saved.Label.Hide();
                    else if (saved.Label.isHidden()) saved.Label.Show();
                    else saved.Label.UpdateNow(true);
                }
            foreach (var entry in binding.Hidden) if (entry.Key != null) entry.Key.SetActive(entry.Value);
            binding.Captions.Clear(); binding.Hidden.Clear(); binding.Actor = null;
            binding.AppliedLock = null; binding.AppliedLockReason = null;
        }
        public static void Forget(CollectionCardVisual visual) { RestoreArtwork(visual); if (visual != null) s_bindings.Remove(visual); }
        public static void Reset()
        {
            PackageBrowser.Reset();
            foreach (CollectionCardVisual visual in new List<CollectionCardVisual>(s_bindings.Keys)) Forget(visual);
            s_bindings.Clear();
            ClearPackageNavigation(); PackagePicker.Close();
            ReleaseNativeActors();
            PackageNativeTile.ReleaseAssets();
            ResetDrag();
            s_componentScopeIds.Clear(); InvalidateComponentIndex();
            PackageArtwork.Clear();
            s_proxies = new ConditionalWeakTable<CollectibleCard, CategoryCatalog.Combo>();
            s_proxyCosts = new ConditionalWeakTable<CollectibleCard, ProxyCost>(); ComboPopup.Close();
        }
    }
}
