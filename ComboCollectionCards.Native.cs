using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Assets;
using HarmonyLib;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static partial class ComboCollectionCards
    {
        private const BindingFlags NativeMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo s_nativeActorList = typeof(CollectionCardActors).GetField("m_cardActors", NativeMembers);
        private static readonly FieldInfo s_visualActors = FindActorsField();
        private static ConditionalWeakTable<CollectionCardActors, CategoryCatalog.Combo> s_actorCombos = new ConditionalWeakTable<CollectionCardActors, CategoryCatalog.Combo>();
        private static readonly Dictionary<CollectionCardVisual, OwnedActors> s_ownedActors = new Dictionary<CollectionCardVisual, OwnedActors>();
        private static CollectionCardVisual s_switchingArtwork;
        internal static TAG_PREMIUM ArtworkPremium(Actor actor) => actor?.GetPremium() ?? TAG_PREMIUM.NORMAL;
        private static readonly Dictionary<Actor, PackageNativeTile> s_packageTiles = new Dictionary<Actor, PackageNativeTile>();
        private static readonly Dictionary<Actor, ComboPortraitMapping> s_portraitMappings = new Dictionary<Actor, ComboPortraitMapping>();
        private static void DestroyComboActor(Actor actor)
        {
            if (s_portraitMappings.TryGetValue(actor, out ComboPortraitMapping mapping)) { s_portraitMappings.Remove(actor); mapping.Dispose(); }
            if (s_packageTiles.TryGetValue(actor, out PackageNativeTile tile)) { s_packageTiles.Remove(actor); tile.Dispose(); }
            actor.Destroy();
        }
        internal const float ArtworkInterval = 3f;
        [ThreadStatic] private static bool s_rendering;
        [ThreadStatic] private static CollectibleCard s_renderCard;
        [ThreadStatic] private static bool s_pinnedCard;
        [ThreadStatic] private static List<RenderSlot> s_renderSlots;
        [ThreadStatic] private static string s_pageMappingDetail;
        private static bool s_pageMappingWarning;

        internal sealed class RenderSlot { internal CollectionCardActors Actors; internal CollectibleCard Card; internal bool Used; internal int Index; }
        public sealed class RenderState { internal bool Rendering, Pinned; internal CollectibleCard Card; internal List<RenderSlot> Slots; }
        internal sealed class CachedArtwork { internal int Index; internal Actor Actor; }
        private static readonly List<KeyValuePair<CollectionCardVisual, OwnedActors>> s_dueArtwork = new List<KeyValuePair<CollectionCardVisual, OwnedActors>>();
        private static bool s_updatingArtwork;
        public sealed class OwnedActors
        {
            internal CollectionCardActors Source, Private;
            internal CategoryCatalog.Combo Combo;
            internal TAG_PREMIUM Premium;
            internal string TemplateAsset;
            internal int ArtworkIndex;
            internal int ArtworkChanges;
            internal int NativeSlot;
            internal bool BindingLogged;
            internal float NextArtworkAt;
            internal List<Actor> Actors = new List<Actor>();
            internal readonly List<CachedArtwork> IdleArtwork = new List<CachedArtwork>(2);
        }
        private static FieldInfo FindActorsField()
        {
            for (Type type = typeof(CollectionCardVisual); type != null; type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(NativeMembers | BindingFlags.DeclaredOnly))
                    if (!field.IsStatic && field.FieldType == typeof(CollectionCardActors)) return field;
            return null;
        }
        private static bool CollectionType(Type type)
        {
            for (; type != null; type = type.DeclaringType)
                if (type.Name.StartsWith("Collectible", StringComparison.Ordinal) || type.Name.StartsWith("Collection", StringComparison.Ordinal)) return true;
            return false;
        }
        internal static IReadOnlyList<MethodInfo> RenderMethods(IEnumerable<Type> types, MethodInfo setActors)
        {
            var graph = new Dictionary<MethodInfo, IReadOnlyList<MethodBase>>();
            foreach (Type type in types)
            {
                if (type == null || !CollectionType(type) || type == typeof(CollectionCardVisual)) continue;
                foreach (MethodInfo method in type.GetMethods(NativeMembers | BindingFlags.DeclaredOnly))
                    if (!method.IsAbstract && !method.ContainsGenericParameters && !method.IsSpecialName)
                        graph[method] = NativePopupDiagnostics.CalledMethods(method);
            }
            var targets = new HashSet<MethodBase> { setActors };
            bool changed;
            do
            {
                changed = false;
                foreach (var entry in graph)
                {
                    if (targets.Contains(entry.Key)) continue;
                    foreach (MethodBase call in entry.Value)
                        if (targets.Contains(call)) { targets.Add(entry.Key); changed = true; break; }
                }
            } while (changed);
            var result = new List<MethodInfo>();
            foreach (var entry in graph) if (targets.Contains(entry.Key)) result.Add(entry.Key);
            return result;
        }
        private static int InstallNativeRendering(HarmonyLib.Harmony harmony)
        {
            try { InstallPackageLoading(harmony); }
            catch (Exception ex) { DebugLog.Exception("Package native loading hook unavailable", ex); }
            if (s_nativeActorList?.FieldType != typeof(List<Actor>) || s_visualActors == null) return 0;
            MethodInfo setActors = typeof(CollectionCardVisual).GetMethod("SetActors", NativeMembers, null,
                new[] { typeof(CollectionCardActors), typeof(CollectionUtils.ViewMode) }, null);
            if (setActors == null) return 0;
            MethodInfo getDefinition = typeof(CollectibleCard).GetMethod("GetEntityDef", NativeMembers);
            MethodInfo getId = typeof(CollectibleCard).GetProperty("CardId", NativeMembers)?.GetGetMethod(true);
            Type[] types;
            try { types = typeof(CollectibleDisplay).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            IReadOnlyList<MethodInfo> renderMethods = RenderMethods(types, setActors);
            if (renderMethods.Count == 0)
            {
                DebugLog.Warning("Combo rendering: no native caller of CollectionCardVisual.SetActors found.");
                return 0;
            }
            HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(ComboCollectionCards).GetMethod(name));
            harmony.Patch(setActors, prefix: Hook(nameof(BindNativeActors)), finalizer: Hook(nameof(FinishNativeActors)));
            MethodInfo hide = typeof(CollectionCardVisual).GetMethod("Hide", NativeMembers);
            if (hide != null) harmony.Patch(hide, finalizer: Hook(nameof(SuspendHiddenActors)));
            MethodInfo show = typeof(CollectionCardVisual).GetMethod("Show", NativeMembers, null, Type.EmptyTypes, null);
            if (show != null) harmony.Patch(show, finalizer: Hook(nameof(ApplyShownActors)));
            MethodInfo destroy = typeof(CollectionCardVisual).GetMethod("OnDestroy", NativeMembers);
            if (destroy != null) harmony.Patch(destroy, finalizer: Hook(nameof(ReleaseHiddenActors)));
            if (getDefinition != null) harmony.Patch(getDefinition, prefix: Hook(nameof(ReadRenderCard)));
            if (getId != null) harmony.Patch(getId, prefix: Hook(nameof(ReadRenderCard)));
            int installed = 0;
            foreach (MethodInfo method in renderMethods)
            {
                try
                {
                    harmony.Patch(method, prefix: Hook(nameof(BeginRender)), finalizer: Hook(nameof(EndRender)));
                    installed++; DebugLog.Msg("Combo render scope: " + method.DeclaringType.FullName + "." + method);
                }
                catch (Exception ex) { DebugLog.Warning("Combo render scope unavailable: " + method + ": " + ex.Message); }
            }
            return installed;
        }
        public static void BeginRender(object __instance, object[] __args, out RenderState __state)
        {
            __state = new RenderState { Rendering = s_rendering, Card = s_renderCard, Pinned = s_pinnedCard, Slots = s_renderSlots };
            s_rendering = true;
            foreach (object value in __args)
                if (value is CollectionUtils.ViewMode mode && mode != CollectionUtils.ViewMode.CARDS)
                { s_rendering = false; s_renderCard = null; s_renderSlots = new List<RenderSlot>(); return; }
            // Validate an explicit card list if supplied. ICollectible may
            // instead describe cosmetics, so its presence is not required.
            // Keep duplicate actor containers used by distinct result slots.
            foreach (object value in __args)
                if (value is IList actors && value.GetType().IsGenericType
                    && value.GetType().GetGenericArguments()[0] == typeof(CollectionCardActors))
                {
                    foreach (object other in __args)
                        if (!ReferenceEquals(other, value) && other is IList cards && cards.Count == actors.Count)
                        {
                            bool hasCard = false;
                            var slots = new List<RenderSlot>();
                            for (int i = 0; i < cards.Count; i++)
                            {
                                if (cards[i] is CollectibleCard) hasCard = true;
                                slots.Add(new RenderSlot { Actors = actors[i] as CollectionCardActors, Card = cards[i] as CollectibleCard, Index = i });
                            }
                            if (hasCard && MatchesPage(actors, slots))
                            { s_renderSlots = slots; s_renderCard = null; s_pinnedCard = false; return; }
                        }
                    // ICollectible can be a separate cosmetic list. For cards,
                    // resolve the actual native page rather than assuming that
                    // both method arguments contain parallel card entries.
                    s_renderSlots = NativePageSlots(actors);
                    s_renderCard = null; s_pinnedCard = false;
                    if (s_renderSlots == null)
                    {
                        s_renderSlots = new List<RenderSlot>(); // Never use last-read-card fallback for a page renderer.
                        if (!s_pageMappingWarning)
                        {
                            s_pageMappingWarning = true;
                            DebugLog.Warning("Combo page mapping unavailable: actors=" + actors.Count
                                + "; " + s_pageMappingDetail + ". Ordinary tiles retain their identity.");
                        }
                    }
                    return;
                }
            foreach (object value in __args) if (value is CollectibleCard card) { s_renderCard = card; s_pinnedCard = true; return; }
            if (s_renderCard == null && __instance != null)
                foreach (FieldInfo field in __instance.GetType().GetFields(NativeMembers))
                    if (!field.IsStatic && field.FieldType == typeof(CollectibleCard) && field.GetValue(__instance) is CollectibleCard card)
                    { s_renderCard = card; s_pinnedCard = true; break; }
        }
        public static void EndRender(RenderState __state)
        { s_rendering = __state.Rendering; s_renderCard = __state.Card; s_pinnedCard = __state.Pinned; s_renderSlots = __state.Slots; }
        public static void ReadRenderCard(CollectibleCard __instance)
        { if (s_rendering && !s_pinnedCard) s_renderCard = __instance; }

        private static bool MatchesPage(IList actors, List<RenderSlot> slots)
        {
            if (slots.Count == 0 || slots.Count > actors.Count) return false;
            for (int i = 0; i < actors.Count; i++)
            {
                var container = actors[i] as CollectionCardActors;
                Actor actor = container?.GetPreferredActor();
                CollectibleCard card = i < slots.Count ? slots[i].Card : null;
                if (card == null) { if (actor != null) return false; continue; }
                if (actor == null || actor.GetEntityDef()?.GetCardId() != card.CardId) return false;
            }
            return true;
        }
        private static object s_pageHintCollection;
        private static readonly Dictionary<string, int> s_pageHints = new Dictionary<string, int>(StringComparer.Ordinal);
        private static void InvalidatePageHints() { s_pageHints.Clear(); s_pageHintCollection = null; }
        private static List<RenderSlot> NativePageSlots(IList actors)
        {
            s_pageMappingDetail = "page manager unavailable";
            try
            {
                object manager = CollectionManager.Get()?.GetCollectibleDisplay()?.GetPageManager();
                if (manager == null) return null;
                s_pageMappingDetail = "page manager=" + manager.GetType().FullName + "; collection unavailable";
                object collection = AccessTools.Field(manager.GetType(), "m_cardsCollection")?.GetValue(manager);
                if (collection == null) return null;
                s_pageMappingDetail = "collection=" + collection.GetType().FullName + "; page API unavailable";
                MethodInfo contents = AccessTools.Method(collection.GetType(), "GetPageContents");
                MethodInfo total = AccessTools.Method(collection.GetType(), "GetTotalNumPages");
                if (contents == null || total == null) return null;
                int current = (int)(manager.GetType().GetProperty("CurrentPageNum", NativeMembers)?.GetValue(manager, null) ?? 1);
                int count = (int)total.Invoke(collection, null);
                if (!ReferenceEquals(s_pageHintCollection, collection)) { s_pageHints.Clear(); s_pageHintCollection = collection; }
                var modelIds = new List<string>();
                foreach (object value in actors)
                    modelIds.Add((value as CollectionCardActors)?.GetPreferredActor()?.GetEntityDef()?.GetCardId() ?? "<empty>");
                string sequence = string.Join("\n", modelIds);
                s_pageMappingDetail = "page=" + current + "/" + count + "; models=[" + string.Join(",", modelIds) + "]";
                // A transition can assemble its destination before CurrentPageNum
                // changes. Validate the full card sequence before accepting it.
                List<RenderSlot> Read(int page)
                {
                    if (page < 1 || page > count || !(contents.Invoke(collection, new object[] { page }) is IEnumerable cards)) return null;
                    var result = new List<RenderSlot>();
                    foreach (object value in cards)
                    {
                        if (value != null && !(value is CollectibleCard))
                        { if (page == current) s_pageMappingDetail += "; unexpected entry=" + value.GetType().FullName; return null; }
                        int i = result.Count;
                        if (i >= actors.Count) return null;
                        result.Add(new RenderSlot { Actors = actors[i] as CollectionCardActors, Card = value as CollectibleCard, Index = i });
                    }
                    if (page == current)
                    {
                        var ids = new List<string>();
                        foreach (RenderSlot slot in result) ids.Add(StableId(slot.Card) ?? "<empty>");
                        s_pageMappingDetail += "; entries=[" + string.Join(",", ids) + "]";
                    }
                    if (!MatchesPage(actors, result)) return null;
                    if (s_pageHints.Count >= 64) s_pageHints.Clear();
                    s_pageHints[sequence] = page;
                    return result;
                }
                List<RenderSlot> slots = Read(current);
                if (slots != null) return slots;
                int hinted = s_pageHints.TryGetValue(sequence, out int known) ? known : -1;
                if (hinted != current && (slots = Read(hinted)) != null) return slots;
                if ((slots = Read(current + 1)) != null || (slots = Read(current - 1)) != null) return slots;
                for (int page = 1; page <= count; page++)
                    if (page != current && page != hinted && page != current + 1 && page != current - 1
                        && (slots = Read(page)) != null) return slots;
            }
            catch (Exception ex) { DebugLog.Warning("Combo native page lookup failed: " + ex.Message); }
            return null;
        }

        private static OwnedActors CopyNativeActors(CollectionCardActors source, CollectionCardVisual visual, CategoryCatalog.Combo combo)
        {
            var result = new OwnedActors { Source = source, Private = (CollectionCardActors)s_clone.Invoke(source, null),
                Combo = combo, Premium = CommonPremium(combo), NextArtworkAt = Time.unscaledTime + ArtworkInterval };
            try
            {
                var actors = (List<Actor>)s_nativeActorList.GetValue(source);
                Actor original = source.GetPreferredActor() ?? actors?.Find(actor => actor != null);
                if (original == null) throw new InvalidOperationException("Native combo source actors are not ready.");
                CollectibleCard component = combo.IsPackage ? null : ArtworkComponent(combo, combo.Cards[0].Id, result.Premium);
                if (!combo.IsPackage && component == null) throw new InvalidOperationException("Combo artwork card is unavailable.");
                // Signature frames require an identity. Resolve it once, so
                // rotating between sets never changes the selected template.
                result.TemplateAsset = ActorNames.GetHandActor(ComboCardTemplate.CardType(combo.Appearance), result.Premium,
                    result.Premium == TAG_PREMIUM.SIGNATURE ? component.CardId : null);
                result.Actors.Add(CreateComboActor(component, original, combo, result.Premium, result.TemplateAsset));
                s_nativeActorList.SetValue(result.Private, result.Actors);
                s_actorCombos.Add(result.Private, combo);
                return result;
            }
            catch { DisposeActors(result); throw; }
        }
        private static Actor CreateComboActor(CollectibleCard component, Actor original, CategoryCatalog.Combo combo, TAG_PREMIUM premium, string templateAsset)
        {
            EntityDef definition = ComboCardTemplate.Definition(combo, combo.IsPackage ? original.GetEntityDef() : component.GetEntityDef());
            GameObject go = AssetLoader.Get().InstantiatePrefab(templateAsset, AssetLoadingOptions.IgnorePrefabPosition);
            try
            {
                Actor actor = go?.GetComponent<Actor>();
                if (actor == null) throw new InvalidOperationException("Combo card actor is unavailable.");
                actor.SetPremium(premium); actor.SetEntityDef(definition); ComboCardTemplate.Prepare(actor, combo.Appearance); actor.UpdateAllComponents();
                // State setup can reset the prefab pose. Match the source's
                // state first, then copy its pose in the same parent space.
                actor.Show(); actor.SetActorState(original.GetActorStateType());
                go.transform.SetParent(original.transform.parent, false);
                go.transform.localPosition = original.transform.localPosition;
                go.transform.localRotation = original.transform.localRotation;
                go.transform.localScale = original.transform.localScale;
                foreach (Transform node in go.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = original.gameObject.layer;
                foreach (Collider collider in go.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                go.SetActive(original.gameObject.activeSelf);
                if (!combo.IsPackage && !string.Equals(ActorNames.GetHandActor(component.GetEntityDef(), premium), templateAsset, StringComparison.Ordinal))
                {
                    try
                    {
                        var mapping = new ComboPortraitMapping(actor, component, premium);
                        s_portraitMappings.Add(actor, mapping); mapping.Refresh();
                    }
                    catch (Exception ex) { DebugLog.Exception("Combo portrait template unavailable", ex); }
                }
                return actor;
            }
            catch { if (go != null) { Actor actor = go.GetComponent<Actor>(); if (actor != null) DestroyComboActor(actor); else UnityEngine.Object.Destroy(go); } throw; }
        }
        private sealed class ActorPose
        {
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
            internal ActorPose(Transform transform)
            { Position = transform.position; Rotation = transform.rotation; Scale = transform.lossyScale; }
            internal void Apply(Transform transform)
            {
                transform.position = Position; transform.rotation = Rotation;
                Vector3 parentScale = transform.parent?.lossyScale ?? Vector3.one;
                float Local(float desired, float parent) => Math.Abs(parent) > .000001f ? desired / parent : desired;
                transform.localScale = new Vector3(Local(Scale.x, parentScale.x), Local(Scale.y, parentScale.y), Local(Scale.z, parentScale.z));
            }
        }
        private static void RestoreActorState(Actor actor, ActorStateType state)
        {
            // Show can clear state-driven effects without clearing the cached state.
            // Re-enter hover even when SetActorState would otherwise skip an unchanged value.
            actor.SetActorState(ActorStateType.CARD_IDLE);
            if (state != ActorStateType.CARD_IDLE) actor.SetActorState(state);
        }
        public static void OnUpdate()
        {
            if (s_updatingArtwork) return;
            s_updatingArtwork = true;
            try { UpdateArtworkCore(); }
            finally { s_dueArtwork.Clear(); s_updatingArtwork = false; }
        }
        private static void UpdateArtworkCore()
        {
            PackageNativeTile.CaptureTemplate();
            UpdatePackageNavigation();
            PollPackageClickSounds();
            UpdateDrag();
            if (s_ownedActors.Count == 0 || (UniversalInputManager.Get()?.IsDialogActive() ?? false)
                || (CollectionInputMgr.Get()?.HasHeldCard() ?? false)) return;
            s_dueArtwork.Clear();
            foreach (var pair in s_ownedActors)
                if (!pair.Value.Combo.IsPackage && pair.Key != null && pair.Key.IsShown() && pair.Value.Combo.Cards.Count >= 2
                    && Time.unscaledTime >= pair.Value.NextArtworkAt) s_dueArtwork.Add(pair);
            foreach (var pair in s_dueArtwork)
            {
                CollectionCardVisual visual = pair.Key; OwnedActors owned = pair.Value;
                if (owned.Combo.IsPackage || visual == null || !visual.IsShown() || owned.Combo.Cards.Count < 2 || Time.unscaledTime < owned.NextArtworkAt) continue;
                owned.NextArtworkAt = Time.unscaledTime + ArtworkInterval;
                Actor replacement = null, original = owned.Actors[0];
                CollectionCardActors previousContainer = owned.Private, replacementContainer = null;
                List<Actor> previousActors = owned.Actors;
                var pose = new ActorPose(original.transform);
                ActorStateType state = original.GetActorStateType();
                bool shown = visual.IsShown();
                CollectionUtils.ViewMode mode = visual.GetVisualType();
                CollectionCardVisual switching = s_switchingArtwork;
                int next = (owned.ArtworkIndex + 1) % owned.Combo.Cards.Count;
                try
                {
                    CollectibleCard component = ArtworkComponent(owned.Combo, owned.Combo.Cards[next].Id, owned.Premium);
                    if (component == null) continue;
                    int cached = owned.IdleArtwork.FindIndex(value => value.Index == next && value.Actor != null);
                    if (cached >= 0)
                    {
                        replacement = owned.IdleArtwork[cached].Actor; owned.IdleArtwork.RemoveAt(cached);
                        replacement.transform.SetParent(original.transform.parent, false);
                        replacement.gameObject.SetActive(original.gameObject.activeSelf);
                    }
                    else replacement = CreateComboActor(component, original, owned.Combo, owned.Premium, owned.TemplateAsset);
                    // Native SetActors may skip an unchanged container and cache
                    // its renderers. Keep the old list intact until the fresh
                    // container and its Show have both succeeded.
                    replacementContainer = (CollectionCardActors)s_clone.Invoke(previousContainer, null);
                    var replacementActors = new List<Actor> { replacement };
                    s_nativeActorList.SetValue(replacementContainer, replacementActors);
                    s_actorCombos.Add(replacementContainer, owned.Combo);
                    RestoreArtwork(visual);
                    s_switchingArtwork = visual;
                    visual.Hide();
                    owned.Private = replacementContainer; owned.Actors = replacementActors;
                    s_ownedActors[visual] = owned;
                    visual.SetActors(replacementContainer, mode);
                    if (visual.GetCollectionCardActors() != replacementContainer
                        || !s_ownedActors.TryGetValue(visual, out OwnedActors accepted) || accepted != owned)
                        throw new InvalidOperationException("Native combo artwork binding was not retained.");
                    if (shown) visual.Show();
                    RestoreActorState(replacement, state);
                    pose.Apply(replacement.transform);
                    Apply(visual);
                    int previousIndex = owned.ArtworkIndex;
                    owned.ArtworkIndex = next;
                    if (++owned.ArtworkChanges <= 2)
                        DebugLog.Msg("Combo artwork changed: " + owned.Combo.Name + "; card=" + component.CardId + "; premium=" + component.PremiumType);
                    s_actorCombos.Remove(previousContainer);
                    foreach (Actor previous in previousActors)
                        if (previous != null)
                        {
                            if (owned.Combo.Cards.Count > 3) { DestroyComboActor(previous); continue; }
                            previous.Hide(); previous.gameObject.SetActive(false);
                            if (owned.IdleArtwork.Count == 2)
                            { DestroyComboActor(owned.IdleArtwork[0].Actor); owned.IdleArtwork.RemoveAt(0); }
                            owned.IdleArtwork.Add(new CachedArtwork { Index = previousIndex, Actor = previous });
                        }
                    previousActors.Clear();
                }
                catch (Exception ex)
                {
                    RestoreArtwork(visual);
                    if (replacement != null)
                    {
                        try
                        {
                            s_switchingArtwork = visual;
                            visual.Hide();
                            owned.Private = previousContainer; owned.Actors = previousActors;
                            s_ownedActors[visual] = owned;
                            s_actorCombos.Remove(previousContainer); s_actorCombos.Add(previousContainer, owned.Combo);
                            visual.SetActors(previousContainer, mode);
                            if (shown) visual.Show();
                            RestoreActorState(original, state);
                            pose.Apply(original.transform);
                        }
                        catch (Exception restore) { DebugLog.Exception("Combo artwork restore failed", restore); }
                        if (replacementContainer != null) s_actorCombos.Remove(replacementContainer);
                        DestroyComboActor(replacement);
                    }
                    Apply(visual);
                    DebugLog.Exception("Combo artwork rotation failed", ex);
                }
                finally { s_switchingArtwork = switching; }
                break; // Spread due native actor switches over separate frames.
            }
        }
        public static void OnLateUpdate()
        {
            // Native actor components can refresh card text after Show or when
            // an asset finishes loading. Reassert captions after those updates,
            // rebuilding text only when its content or layout actually changed.
            foreach (var pair in s_ownedActors)
                if (pair.Key != null && pair.Key.IsShown())
                {
                    Apply(pair.Key);
                    foreach (Actor actor in pair.Value.Actors)
                        if (s_portraitMappings.TryGetValue(actor, out ComboPortraitMapping mapping)) mapping.Refresh();
                }
        }
        public static void BindNativeActors(CollectionCardVisual __instance, ref CollectionCardActors __0, out OwnedActors __state)
        {
            s_ownedActors.TryGetValue(__instance, out __state);
            CollectionCardActors source = __0;
            if (source == null) return;
            CategoryCatalog.Combo combo = null;
            int nativeSlot = -1;
            bool slotFound = false;
            if (s_renderSlots != null)
                foreach (RenderSlot slot in s_renderSlots)
                    if (!slot.Used && ReferenceEquals(slot.Actors, source))
                    {
                        slot.Used = true; slotFound = true; nativeSlot = slot.Index;
                        if (slot.Card != null) s_proxies.TryGetValue(slot.Card, out combo);
                        break;
                    }
            if (!slotFound && s_renderSlots == null && s_rendering && s_renderCard != null) s_proxies.TryGetValue(s_renderCard, out combo);
            if (combo == null) s_actorCombos.TryGetValue(source, out combo);
            RestoreArtwork(__instance); s_bindings.Remove(__instance);
            if (combo == null) { s_ownedActors.Remove(__instance); return; }
            if (__state != null && source == __state.Private)
            {
                s_bindings[__instance] = new Binding { Combo = combo }; return;
            }
            OwnedActors owned;
            try { owned = TakePreparedPackage(source, combo) ?? CopyNativeActors(source, __instance, combo); }
            catch (Exception ex)
            {
                // Keep the page usable and preserve combo input routing without
                // ever changing captions on shared native models after a failure.
                s_ownedActors.Remove(__instance);
                s_bindings[__instance] = new Binding { Combo = combo, SkipArtwork = true };
                DebugLog.Exception("Combo tile models unavailable: " + combo.Name, ex);
                return;
            }
            s_ownedActors[__instance] = owned;
            owned.NativeSlot = nativeSlot;
            s_bindings[__instance] = new Binding { Combo = combo };
            __0 = owned.Private;
        }
        public static void FinishNativeActors(CollectionCardVisual __instance, OwnedActors __state)
        {
            PackageNativeTile.Observe(__instance);
            CollectionCardActors actual = __instance.GetCollectionCardActors();
            if (s_ownedActors.TryGetValue(__instance, out OwnedActors current) && current.Private != actual)
            {
                // Never keep combo captions/input on a shared native actor when
                // SetActors did not retain the private replacement.
                Forget(__instance); s_ownedActors.Remove(__instance);
                if (current != __state) DisposeActors(current);
                DebugLog.Warning("Combo model replacement rejected; native tile preserved: " + actual?.GetPreferredActor()?.GetEntityDef()?.GetCardId());
            }
            if (__state != null)
            {
                if (__state.Private == actual) s_ownedActors[__instance] = __state;
                else DisposeActors(__state);
            }
            if (s_ownedActors.TryGetValue(__instance, out OwnedActors bound) && bound.Private == actual && !bound.BindingLogged)
            {
                bound.BindingLogged = true;
                DebugLog.Msg("Combo tile bound: " + bound.Combo.Name + "; slot=" + bound.NativeSlot
                    + "; card=" + actual.GetPreferredActor()?.GetEntityDef()?.GetCardId()
                    + "; premium=" + actual.GetPreferredActor()?.GetPremium() + "; artworkPremium=" + ArtworkPremium(actual.GetPreferredActor()) + "; actors=" + bound.Actors.Count);
            }
            Apply(__instance);
        }
        private static void DisposeActors(OwnedActors owned)
        {
            if (owned.Private != null) s_actorCombos.Remove(owned.Private);
            foreach (Actor actor in owned.Actors) if (actor != null) DestroyComboActor(actor);
            owned.Actors.Clear();
            foreach (CachedArtwork cached in owned.IdleArtwork) if (cached.Actor != null) DestroyComboActor(cached.Actor);
            owned.IdleArtwork.Clear();
        }
        public static void SuspendHiddenActors(CollectionCardVisual __instance)
        {
            if (__instance != null && s_ownedActors.TryGetValue(__instance, out OwnedActors owned))
                foreach (Actor actor in owned.Actors)
                    if (actor != null && s_packageTiles.TryGetValue(actor, out PackageNativeTile tile)) tile.Hide();
            Forget(__instance);
        }
        public static void ApplyShownActors(CollectionCardVisual __instance) { PackageNativeTile.Observe(__instance); Apply(__instance); }
        public static void ReleaseHiddenActors(CollectionCardVisual __instance)
        {
            if (__instance == s_switchingArtwork) return;
            Forget(__instance);
            if (!s_ownedActors.TryGetValue(__instance, out OwnedActors owned)) return;
            s_ownedActors.Remove(__instance);
            if (__instance.GetCollectionCardActors() == owned.Private) s_visualActors.SetValue(__instance, owned.Source);
            DisposeActors(owned);
        }
        private static void ReleaseNativeActors()
        {
            s_packageLoadGeneration++;
            ReleasePreparedPackages();
            foreach (CollectionCardVisual visual in new List<CollectionCardVisual>(s_ownedActors.Keys)) ReleaseHiddenActors(visual);
            s_actorCombos = new ConditionalWeakTable<CollectionCardActors, CategoryCatalog.Combo>();
            s_rendering = false; s_renderCard = null; s_pinnedCard = false;
            s_switchingArtwork = null;
            s_renderSlots = null; InvalidatePageHints();
            s_pageMappingWarning = false;
            s_pageMappingDetail = null;
        }
    }
}
