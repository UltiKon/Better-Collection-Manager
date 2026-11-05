using System;
using System.Collections.Generic;
using Assets;
using UnityEngine;

namespace BetterCollectionManager
{
    /// <summary>Presentation and input only; never changes inventory counts or card premiums.</summary>
    internal sealed class MixedPremiumCard : MonoBehaviour
    {
        private const float StackFrontOffset = 0.04f;
        private const float StackRearOffset = 0.06f;
        private static readonly Dictionary<CollectionCardActors, MixedPremiumCard> s_cards =
            new Dictionary<CollectionCardActors, MixedPremiumCard>();
        private static readonly HashSet<string> s_failedCards = new HashSet<string>();
        private static bool s_refreshPending;
        public static bool Ready { get; set; }

        private CollectionCardVisual m_visual;
        private CollectionCardActors m_actors;
        private Actor m_primary, m_secondary, m_normal, m_golden, m_selected;
        private Actor m_pressed;
        private string m_cardId;
        private float m_upDirection;
        private float m_rightDirection;
        private float m_splitFraction;
        private bool m_stacking;
        private Actor m_front, m_back;
        private Vector3 m_primaryBase, m_primaryStackPosition;
        private Vector3 m_primaryBaseScale, m_primaryStackScale;
        private bool m_hasStackPosition;
        private float m_stackBoundary;
        private sealed class StackChrome
        {
            public Vector3 Original, Lifted;
        }
        private readonly Dictionary<Transform, StackChrome> m_stackChrome = new Dictionary<Transform, StackChrome>();
        private readonly Dictionary<Component, MeshCopy> m_meshes = new Dictionary<Component, MeshCopy>();
        private readonly Dictionary<Renderer, bool> m_effects = new Dictionary<Renderer, bool>();
        private readonly Dictionary<GameObject, bool> m_hiddenObjects = new Dictionary<GameObject, bool>();
        private readonly Dictionary<ProjectedShadow, bool> m_shadows = new Dictionary<ProjectedShadow, bool>();
        private readonly Dictionary<Collider, bool> m_actorColliders = new Dictionary<Collider, bool>();
        private readonly Dictionary<UberText, bool> m_hiddenText = new Dictionary<UberText, bool>();
        private readonly Dictionary<UberText, int> m_manaQueues = new Dictionary<UberText, int>();
        private sealed class TextOutputOrder { public int Layer, Order, GameLayer; }
        private readonly Dictionary<Renderer, TextOutputOrder> m_manaOutputs = new Dictionary<Renderer, TextOutputOrder>();
        private readonly Dictionary<Transform, StackChrome> m_manaPositions = new Dictionary<Transform, StackChrome>();
        private readonly HashSet<Transform> m_unclippedRoots = new HashSet<Transform>();

        private sealed class ActorParts
        {
            internal Transform[] Nodes;
            internal int[] Children;
            internal Renderer[] Renderers;
            internal UberText[] Text;
            internal Collider[] Colliders;
            internal MeshFilter[] Filters;
            internal SkinnedMeshRenderer[] Skins;
            internal ProjectedShadow[] Shadows;
            internal ActorStateType State;
            internal UberText Name, Cost;
            internal int Pass = -1;
        }
        private int m_partsPass;
        private readonly Dictionary<Actor, ActorParts> m_parts = new Dictionary<Actor, ActorParts>();
        private readonly Dictionary<GameObject, Renderer[]> m_partRenderers = new Dictionary<GameObject, Renderer[]>();
        private readonly List<Material> m_scanMaterials = new List<Material>();
        private ActorParts Parts(Actor actor)
        {
            if (!m_parts.TryGetValue(actor, out ActorParts parts)) m_parts[actor] = parts = new ActorParts();
            if (parts.Pass == m_partsPass) return parts;
            parts.Pass = m_partsPass;
            bool changed = parts.Nodes == null || parts.State != actor.GetActorStateType()
                || parts.Name != actor.GetNameText() || parts.Cost != actor.GetCostText();
            if (!changed)
                for (int i = 0; i < parts.Nodes.Length; i++)
                    if (parts.Nodes[i] == null || parts.Nodes[i].childCount != parts.Children[i]) { changed = true; break; }
            if (!changed)
            {
                foreach (Renderer renderer in parts.Renderers) if (renderer == null) { changed = true; break; }
                if (!changed) foreach (MeshFilter filter in parts.Filters) if (filter == null) { changed = true; break; }
                if (!changed) foreach (Collider collider in parts.Colliders) if (collider == null) { changed = true; break; }
            }
            if (!changed) return parts;
            parts.Nodes = actor.GetComponentsInChildren<Transform>(true); parts.Children = new int[parts.Nodes.Length];
            for (int i = 0; i < parts.Nodes.Length; i++) parts.Children[i] = parts.Nodes[i].childCount;
            parts.Renderers = actor.GetComponentsInChildren<Renderer>(true); parts.Text = actor.GetComponentsInChildren<UberText>(true);
            parts.Colliders = actor.GetComponentsInChildren<Collider>(true); parts.Filters = actor.GetComponentsInChildren<MeshFilter>(true);
            parts.Skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true); parts.Shadows = actor.GetComponentsInChildren<ProjectedShadow>(true);
            parts.State = actor.GetActorStateType(); parts.Name = actor.GetNameText(); parts.Cost = actor.GetCostText();
            m_partRenderers.Clear(); return parts;
        }
        private Renderer[] PartRenderers(GameObject go)
        {
            if (!m_partRenderers.TryGetValue(go, out Renderer[] renderers))
                m_partRenderers[go] = renderers = go.GetComponentsInChildren<Renderer>(true);
            return renderers;
        }

        private sealed class MeshCopy
        {
            public Mesh Original, Clipped;
        }

        public static bool CanMerge(string cardId) => Ready && !s_failedCards.Contains(cardId);

        public static bool TryGetSelected(CollectionCardActors actors, out Actor actor)
        {
            actor = null;
            if (actors == null || !s_cards.TryGetValue(actors, out MixedPremiumCard card) || card == null)
                return false;
            actor = card.m_selected;
            return actor != null;
        }

        public static void Attach(CollectionCardVisual visual)
        {
            if (!Ready || visual == null || !visual.IsShown()
                || visual.GetVisualType() != CollectionUtils.ViewMode.CARDS
                || !PreferredQualityCollapse.TryGetMixed(visual.CardId, out MixedPremiumPair pair)
                || !CanMerge(visual.CardId))
                return;

            MixedPremiumCard card = visual.GetComponent<MixedPremiumCard>();
            if (card == null) card = visual.gameObject.AddComponent<MixedPremiumCard>();
            card.Cleanup();
            card.m_visual = visual;
            card.m_cardId = visual.CardId;
            card.m_stacking = pair.IsStacking;
            try
            {
                card.m_actors = visual.GetCollectionCardActors();
                card.m_primary = visual.GetActor();
                if (card.m_primary == null)
                    throw new InvalidOperationException("Mixed collection tile has no actor");
                card.m_upDirection = card.ResolveUpDirection();
                card.m_rightDirection = card.ResolveRightDirection();
                TAG_PREMIUM other = pair.IsStacking
                    ? (card.m_primary.GetPremium() == pair.Preferred.PremiumType ? pair.Secondary.PremiumType : pair.Preferred.PremiumType)
                    : card.m_primary.GetPremium() == TAG_PREMIUM.GOLDEN
                    ? TAG_PREMIUM.NORMAL : TAG_PREMIUM.GOLDEN;
                EntityDef def = card.m_primary.GetEntityDef();
                string prefab = ActorNames.GetHandActor(def, other);
                GameObject go = AssetLoader.Get().InstantiatePrefab(prefab, AssetLoadingOptions.IgnorePrefabPosition);
                if (go == null) throw new InvalidOperationException("Could not load the second card actor");
                card.m_secondary = go.GetComponent<Actor>();
                if (card.m_secondary == null)
                {
                    UnityEngine.Object.Destroy(go);
                    throw new InvalidOperationException("Second card prefab has no actor");
                }
                card.m_secondary.SetPremium(other);
                card.m_secondary.SetEntityDef(def);
                card.m_secondary.UpdateAllComponents();
                card.m_secondary.transform.SetParent(visual.transform, false);
                card.CopyTransform();
                foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = card.m_primary.gameObject.layer;
                // The collection visual owns one hit area. The second actor is artwork.
                card.DisableActorColliders(card.m_secondary);
                card.m_secondary.Show();
                card.m_secondary.SetActorState(ActorStateType.CARD_IDLE);
                card.m_golden = card.m_primary.GetPremium() == TAG_PREMIUM.GOLDEN ? card.m_primary : card.m_secondary;
                card.m_normal = card.m_primary.GetPremium() == TAG_PREMIUM.NORMAL ? card.m_primary : card.m_secondary;
                card.m_front = pair.IsStacking && card.m_primary.GetPremium() != pair.Preferred.PremiumType
                    ? card.m_secondary : card.m_primary;
                card.m_back = card.m_front == card.m_primary ? card.m_secondary : card.m_primary;
                card.m_selected = card.m_primary;
                card.CopyTransform();
                if (card.m_stacking) card.ConfigureStack();
                else
                {
                    card.m_splitFraction = card.ResolveSplitFraction();
                    card.ClipActors();
                }
                s_cards[card.m_actors] = card;
                card.enabled = true;
            }
            catch (Exception ex)
            {
                card.Fail(ex);
            }
        }

        public static void Release(CollectionCardVisual visual)
        {
            if (visual != null) visual.GetComponent<MixedPremiumCard>()?.Cleanup();
        }

        public static void Choose(CollectionCardVisual visual, bool press, bool usePress)
        {
            if (visual == null || ComboCollectionCards.IsCombo(visual)) return;
            MixedPremiumCard card = visual.GetComponent<MixedPremiumCard>();
            if (card == null || card.m_selected == null) return;
            try
            {
                if (usePress && card.m_pressed != null) card.Select(card.m_pressed);
                else card.ChooseAtPointer();
                if (press) card.m_pressed = card.m_selected;
                else if (usePress) card.m_pressed = null;
            }
            catch (Exception ex) { card.Fail(ex); }
        }

        private void ChooseAtPointer()
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_visual.gameObject);
            if (camera == null) return;
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(m_visual.transform.up, m_visual.transform.TransformPoint(m_visual.m_boxColliderCenter));
            if (!plane.Raycast(ray, out float distance)) return;
            Vector3 point = m_visual.transform.InverseTransformPoint(ray.GetPoint(distance));
            Vector3 center = m_visual.m_boxColliderCenter, size = m_visual.m_boxColliderSize;
            if (Mathf.Abs(point.x - center.x) > size.x * 0.5f
                || Mathf.Abs(point.z - center.z) > size.z * 0.5f)
                return;
            if (m_stacking) Select((point.x - center.x) * m_rightDirection < m_stackBoundary ? m_back : m_front);
            else Select(MixedPremiumPair.IsGoldenHalf(point.z, center.z, size.z, m_upDirection, m_splitFraction)
                ? m_golden : m_normal);
        }

        private float ResolveUpDirection()
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_visual.gameObject);
            if (camera == null)
                throw new InvalidOperationException("Cannot orient a mixed card without its projection camera");
            Vector3 center = m_visual.m_boxColliderCenter;
            Vector3 alongZ = center + new Vector3(0f, 0f, m_visual.m_boxColliderSize.z);
            float screenCenter = camera.WorldToScreenPoint(m_visual.transform.TransformPoint(center)).y;
            float screenZ = camera.WorldToScreenPoint(m_visual.transform.TransformPoint(alongZ)).y;
            return screenZ >= screenCenter ? 1f : -1f;
        }

        private float ResolveRightDirection()
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_visual.gameObject);
            if (camera == null)
                throw new InvalidOperationException("Cannot orient a stacked card without its projection camera");
            Vector3 center = m_visual.m_boxColliderCenter;
            Vector3 alongX = center + new Vector3(m_visual.m_boxColliderSize.x, 0f, 0f);
            float screenCenter = camera.WorldToScreenPoint(m_visual.transform.TransformPoint(center)).x;
            float screenX = camera.WorldToScreenPoint(m_visual.transform.TransformPoint(alongX)).x;
            return screenX >= screenCenter ? 1f : -1f;
        }

        private float ResolveSplitFraction()
        {
            GameObject banner = m_golden.m_nameBannerMesh;
            if (banner == null) return MixedPremiumPair.NameBannerFraction;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            foreach (Renderer renderer in banner.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled) continue;
                GetVerticalRange(renderer.bounds, out float low, out float high);
                bottom = Mathf.Min(bottom, low);
                top = Mathf.Max(top, high);
            }
            if (float.IsPositiveInfinity(bottom)) return MixedPremiumPair.NameBannerFraction;

            // Cut the surrounding frame at the banner's center. The entire
            // Golden banner is excluded from clipping, covering the seam.
            float fraction = 0.5f + (bottom + top) * 0.5f / m_visual.m_boxColliderSize.z;
            return fraction > 0.1f && fraction < 0.8f ? fraction : MixedPremiumPair.NameBannerFraction;
        }

        private void GetVerticalRange(Bounds world, out float bottom, out float top)
        {
            bottom = float.PositiveInfinity;
            top = float.NegativeInfinity;
            Vector3 half = world.size * 0.5f;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = world.center + new Vector3(half.x * x, half.y * y, half.z * z);
                        float up = (m_visual.transform.InverseTransformPoint(corner).z
                            - m_visual.m_boxColliderCenter.z) * m_upDirection;
                        bottom = Mathf.Min(bottom, up);
                        top = Mathf.Max(top, up);
                    }
        }

        private void Select(Actor selected)
        {
            if (selected == null || selected == m_selected) return;
            // Native OnOver/OnOut runs on the previously selected half. Keep
            // that current interaction state when switching actors; taking
            // the new half's stale idle state would erase a fresh hover glow.
            ActorStateType state = m_selected.GetActorStateType();
            if (selected.GetActorStateType() != state) selected.SetActorState(state);
            m_selected = selected;
            // Native count/lock calculations now see this half's real actor.
            // Exhausting Golden must not leave the Normal half locked as well.
            m_visual.OnDoneCrafting();
            CollectionManager.Get()?.GetCollectibleDisplay()?.UpdateCurrentPageCardLocks();
        }

        private void LateUpdate()
        {
            if (m_selected == null) return;
            if (m_primary == null || m_visual == null || !m_visual.IsShown()) { Cleanup(); return; }
            try
            {
                unchecked { m_partsPass++; }
                // Do not change the chosen version during a drag or inspection.
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1)
                    && !CraftingManager.GetIsInCraftingMode()
                    && !(CollectionInputMgr.Get()?.HasHeldCard() ?? false))
                    ChooseAtPointer();
                ActorStateType state = m_selected.GetActorStateType();
                if (m_primary.GetActorStateType() != state) m_primary.SetActorState(state);
                if (m_secondary.GetActorStateType() != state) m_secondary.SetActorState(state);
                CopyTransform();
                // Native text/effects can replace their meshes after Show.
                if (m_stacking) ConfigureStack();
                else ClipActors();
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void CopyTransform()
        {
            // Remove our last offset before copying native hover/idle transforms.
            if (m_stacking && m_hasStackPosition && m_primary.transform.localPosition.Equals(m_primaryStackPosition))
                m_primary.transform.localPosition = m_primaryBase;
            if (m_stacking && m_hasStackPosition && m_primary.transform.localScale.Equals(m_primaryStackScale))
                m_primary.transform.localScale = m_primaryBaseScale;
            if (m_stacking)
            {
                m_primaryBase = m_primary.transform.localPosition;
                m_primaryBaseScale = m_primary.transform.localScale;
            }
            m_secondary.transform.localPosition = m_primary.transform.localPosition;
            m_secondary.transform.localRotation = m_primary.transform.localRotation;
            m_secondary.transform.localScale = m_primary.transform.localScale;
            // Normal/Golden prefabs can have different mesh origins beneath
            // identical actor transforms. Align the actual frame centers in
            // collection space, without moving text or individual decorations.
            Renderer primaryFrame = m_primary.GetMeshRenderer();
            Renderer secondaryFrame = m_secondary.GetMeshRenderer();
            if (primaryFrame != null && secondaryFrame != null
                && primaryFrame.bounds.size.x > 0f && secondaryFrame.bounds.size.x > 0f)
            {
                float primaryX = m_visual.transform.InverseTransformPoint(primaryFrame.bounds.center).x;
                float secondaryX = m_visual.transform.InverseTransformPoint(secondaryFrame.bounds.center).x;
                Vector3 position = m_secondary.transform.localPosition;
                position.x += primaryX - secondaryX;
                m_secondary.transform.localPosition = position;
            }
        }

        private void ConfigureStack()
        {
            // Imik-style fan: lower priority peeks out on the left; the full
            // preferred card sits on top. Keep the native tile's hit area.
            float width = m_visual.m_boxColliderSize.x;
            Vector3 front = m_front.transform.localPosition;
            Vector3 back = m_back.transform.localPosition;
            // Collection space can be mirrored relative to the camera. Both
            // the fan and its hit boundary must follow screen-left/right.
            front.x += width * StackFrontOffset * m_rightDirection;
            back.x -= width * StackRearOffset * m_rightDirection;
            // Raise both frames clear of the collection paper, as in Imik.
            front.y += 0.30f;
            back.y += 0.18f;
            m_front.transform.localPosition = front;
            m_back.transform.localPosition = back;
            // Mirror only the rear artwork about the vertical card axis, so
            // its mana cutout sits under the complete preferred card.
            Renderer backFrame = m_back.GetMeshRenderer();
            float frameCenter = backFrame == null ? 0f
                : m_visual.transform.InverseTransformPoint(backFrame.bounds.center).x;
            Vector3 rearScale = m_back.transform.localScale;
            rearScale.x = -rearScale.x;
            m_back.transform.localScale = rearScale;
            if (backFrame != null)
            {
                Vector3 mirrored = m_back.transform.localPosition;
                mirrored.x += frameCenter - m_visual.transform.InverseTransformPoint(backFrame.bounds.center).x;
                m_back.transform.localPosition = mirrored;
            }
            m_primaryStackPosition = m_primary.transform.localPosition;
            m_primaryStackScale = m_primary.transform.localScale;
            m_hasStackPosition = true;
            m_stackBoundary = -width * 0.5f + width * (StackFrontOffset + StackRearOffset);
            // Only the front card supplies text and decorations; the back is
            // a complete native frame, without mesh clipping or shared edits.
            foreach (UberText label in Parts(m_back).Text) SuppressStackText(label);
            SuppressObject(m_back.m_manaObject);
            // Some prefabs keep the mana gem/shadow outside m_manaObject.
            foreach (Transform child in Parts(m_back).Nodes)
            {
                string name = child.gameObject.name;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Equals("ManaGem", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("Gem_Mana", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("ManaObject", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("CostGem_DropShadow", StringComparison.OrdinalIgnoreCase) >= 0
                    || IsRuneNode(child))
                    SuppressObject(child.gameObject);
            }
            SuppressObject(m_back.GetAttackObject()?.gameObject);
            SuppressObject(m_back.GetHealthObject()?.gameObject);
            SuppressObject(m_back.m_rarityGemMesh);
            SuppressStackRuneEffects(m_front);
            SuppressStackRuneEffects(m_back);
            // Native hover/Show can re-enable actor colliders. PegUI requires
            // the hit on CollectionCardVisual itself, not an actor child.
            DisableActorColliders(m_front);
            DisableActorColliders(m_back);
            ConfigureManaTextLayer();
            LiftStackChrome(m_visual.m_count?.transform);
            LiftStackChrome(m_visual.m_cardLock?.transform);
            LiftStackChrome(m_visual.m_newCardCallout?.transform);
            LiftStackChrome(m_visual.m_favoriteBanner?.transform);
        }

        private void DisableActorColliders(Actor actor)
        {
            foreach (Collider collider in Parts(actor).Colliders)
            {
                if (collider.gameObject == m_visual.gameObject) continue;
                if (!m_actorColliders.ContainsKey(collider)) m_actorColliders.Add(collider, collider.enabled);
                collider.enabled = false;
            }
        }

        private void SuppressStackText(UberText label)
        {
            if (!m_hiddenText.ContainsKey(label)) m_hiddenText.Add(label, label.isHidden());
            // Hide controls UberText's native output, including texture planes
            // outside this object's transform subtree.
            label.Hide();
            SuppressObject(label.gameObject);
        }

        private void ConfigureManaTextLayer()
        {
            UberText cost = m_front.GetCostText();
            Renderer frame = m_front.GetMeshRenderer();
            if (cost == null || frame == null) return;
            foreach (var entry in m_manaPositions)
            {
                if (entry.Key == null) continue;
                if (entry.Key.localPosition.Equals(entry.Value.Lifted)) entry.Key.localPosition = entry.Value.Original;
                else entry.Value.Original = entry.Key.localPosition;
            }
            if (!m_manaQueues.ContainsKey(cost)) m_manaQueues.Add(cost, cost.RenderQueue);
            RememberManaOutput(cost);
            // RenderQueue is a native text-order increment, not an absolute
            // material queue. Keep the mana digit in the card's drawing order.
            int queue = Math.Min(m_manaQueues[cost], frame.sortingOrder);
            if (cost.RenderQueue != queue) { cost.RenderQueue = queue; cost.UpdateNow(true); }
            RememberManaOutput(cost);
            foreach (Renderer output in CounterTextLayout.VisibleRenderers(cost))
            {
                output.sortingLayerID = frame.sortingLayerID;
                output.sortingOrder = Math.Min(m_manaOutputs[output].Order, frame.sortingOrder);
                output.gameObject.layer = m_front.gameObject.layer;
            }
            ConfigureManaDepth(cost, frame);
        }

        private void ConfigureManaDepth(UberText cost, Renderer frame)
        {
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_front.gameObject);
            if (camera == null) return;
            Renderer[] outputs = CounterTextLayout.VisibleRenderers(cost);
            float gemDepth = float.PositiveInfinity;
            if (m_front.m_manaObject != null)
                foreach (Renderer gem in m_front.m_manaObject.GetComponentsInChildren<Renderer>(false))
                    if (gem.enabled && gem.GetComponentInParent<UberText>() == null
                        && Array.IndexOf(outputs, gem) < 0)
                        gemDepth = Mathf.Min(gemDepth, NearestDepth(camera, gem.bounds));
            if (float.IsInfinity(gemDepth)) gemDepth = NearestDepth(camera, frame.bounds);
            if (float.IsInfinity(gemDepth) || gemDepth <= 0.05f) return;
            // Native mana text can sit far above the card mesh. Moving the
            // whole actor lifts it through the book cover even at queue zero.
            // Keep the actual visible glyph/texture plane just over the gem.
            float depth = gemDepth - 0.001f;
            float childDepth = float.PositiveInfinity;
            foreach (Renderer output in outputs)
                if (output.transform.IsChildOf(cost.transform))
                    childDepth = Mathf.Min(childDepth, NearestDepth(camera, output.bounds));
            LowerManaOutput(camera, cost.transform, childDepth, depth);
            foreach (Renderer output in outputs)
                if (!output.transform.IsChildOf(cost.transform))
                    LowerManaOutput(camera, output.transform, NearestDepth(camera, output.bounds), depth);
        }

        private void LowerManaOutput(Camera camera, Transform target, float currentDepth, float depth)
        {
            if (!(currentDepth < depth)) return;
            if (!m_manaPositions.TryGetValue(target, out StackChrome state))
            {
                state = new StackChrome { Original = target.localPosition };
                m_manaPositions.Add(target, state);
            }
            Vector3 screen = camera.WorldToScreenPoint(target.position);
            screen.z += depth - currentDepth;
            target.position = camera.ScreenToWorldPoint(screen);
            state.Lifted = target.localPosition;
        }

        private static float NearestDepth(Camera camera, Bounds bounds)
        {
            float depth = float.PositiveInfinity;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                depth = Mathf.Min(depth, camera.WorldToScreenPoint(new Vector3(
                    x == 0 ? bounds.min.x : bounds.max.x, y == 0 ? bounds.min.y : bounds.max.y,
                    z == 0 ? bounds.min.z : bounds.max.z)).z);
            return depth;
        }

        private void RememberManaOutput(UberText cost)
        {
            foreach (Renderer output in CounterTextLayout.VisibleRenderers(cost))
                if (!m_manaOutputs.ContainsKey(output))
                    m_manaOutputs.Add(output, new TextOutputOrder { Layer = output.sortingLayerID,
                        Order = output.sortingOrder, GameLayer = output.gameObject.layer });
        }

        private static bool IsRuneNode(Transform node)
        {
            string name = node.gameObject.name ?? "";
            return name.StartsWith("Rune", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("RuneHolder", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Rune_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasRuneAncestor(Transform node, Transform actor)
        {
            for (; node != null && node != actor; node = node.parent)
                if (IsRuneNode(node)) return true;
            return false;
        }

        private void SuppressStackRuneEffects(Actor actor)
        {
            foreach (Renderer renderer in Parts(actor).Renderers)
            {
                string name = renderer.gameObject.name ?? "";
                bool runeHover = HasRuneAncestor(renderer.transform, actor.transform)
                    && (name.IndexOf("Hover_Highlight", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.Equals("HoverHighlight", StringComparison.OrdinalIgnoreCase)
                        || name.IndexOf("RuneHover", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("HolderGlow_Tutorial", StringComparison.OrdinalIgnoreCase) >= 0);
                bool warmGlow = false;
                renderer.GetSharedMaterials(m_scanMaterials);
                foreach (Material material in m_scanMaterials)
                    if (material != null && (material.name ?? "").IndexOf("Glow_Warm", StringComparison.OrdinalIgnoreCase) >= 0)
                        warmGlow = true;
                if (runeHover || warmGlow || name.IndexOf("CardBackGlow", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Orange_Fill", StringComparison.OrdinalIgnoreCase) >= 0)
                    SuppressObject(renderer.gameObject);
            }
        }

        private void LiftStackChrome(Transform target)
        {
            if (target == null) return;
            Camera camera = CameraUtils.FindProjectionCameraForObject(m_visual.gameObject);
            if (camera == null) return;
            if (!m_stackChrome.TryGetValue(target, out StackChrome state))
            {
                state = new StackChrome { Original = target.localPosition, Lifted = target.localPosition };
                m_stackChrome.Add(target, state);
            }
            else if (!target.localPosition.Equals(state.Lifted)) state.Original = target.localPosition;
            Vector3 world = target.parent == null ? state.Original : target.parent.TransformPoint(state.Original);
            Vector3 screen = camera.WorldToScreenPoint(world);
            if (screen.z <= 0.05f) return;
            // Camera depth preserves the counter/lock's screen position.
            screen.z = Mathf.Max(0.05f, screen.z - 0.32f);
            target.position = camera.ScreenToWorldPoint(screen);
            state.Lifted = target.localPosition;
        }

        private void ClipActors()
        {
            // Text geometry is rebuilt independently of the card frame. Give
            // every label one owner instead of trying to clip its glyphs.
            if (m_golden.m_nameBannerMesh != null)
            {
                m_unclippedRoots.Add(m_golden.m_nameBannerMesh.transform);
                SuppressObject(m_normal.m_nameBannerMesh);
            }
            ConfigureShadows();
            ConfigureText(m_golden, true);
            ConfigureText(m_normal, false);
            SuppressObject(m_golden.GetAttackObject()?.gameObject);
            SuppressObject(m_golden.GetHealthObject()?.gameObject);
            ClipActor(m_golden, true);
            ClipActor(m_normal, false);
        }

        private void ConfigureShadows()
        {
            // Use one complete native Golden contact shadow below the entire
            // tile. Two full shadows would darken one another; clipping either
            // at the seam would introduce a hard edge.
            bool fullShadow = false;
            foreach (Transform child in Parts(m_golden).Nodes)
                if (child.gameObject.CompareTag("FakeShadow") || child.gameObject.CompareTag("FakeShadowUnique"))
                {
                    fullShadow |= child.gameObject.CompareTag("FakeShadow");
                    m_unclippedRoots.Add(child);
                    foreach (Renderer renderer in PartRenderers(child.gameObject))
                        if (!m_effects.ContainsKey(renderer)) m_effects[renderer] = renderer.enabled;
                }
            if (fullShadow) CollectionCardVisual.ShowActorShadow(m_golden, true);
            for (int actorIndex = 0; actorIndex < 2; actorIndex++)
            {
                Actor actor = actorIndex == 0 ? m_golden : m_normal;
                foreach (Transform child in Parts(actor).Nodes)
                {
                    bool fake = child.gameObject.CompareTag("FakeShadow") || child.gameObject.CompareTag("FakeShadowUnique");
                    bool projected = child.gameObject.name.EndsWith("_ContactShadowRenderPlane", StringComparison.Ordinal);
                    if (fake || projected) m_unclippedRoots.Add(child);
                    if ((fake && actor == m_normal) || (projected && fullShadow)) SuppressObject(child.gameObject);
                }
                foreach (ProjectedShadow shadow in Parts(actor).Shadows)
                {
                    if (!m_shadows.ContainsKey(shadow)) m_shadows[shadow] = shadow.enabled;
                    // Frames without a static contact shadow keep the native
                    // soft projected shadows from both visible actor halves.
                    shadow.enabled = !fullShadow && m_shadows[shadow];
                }
            }
        }

        private void ConfigureText(Actor actor, bool golden)
        {
            UberText name = actor.GetNameText();
            UberText cost = actor.GetCostText();
            foreach (UberText text in Parts(actor).Text)
            {
                m_unclippedRoots.Add(text.transform);
                bool goldenLabel = text == name || text == cost;
                if (golden != goldenLabel) SuppressObject(text.gameObject);
            }
        }

        private void SuppressObject(GameObject go)
        {
            if (go == null) return;
            m_unclippedRoots.Add(go.transform);
            if (!m_hiddenObjects.ContainsKey(go)) m_hiddenObjects[go] = go.activeSelf;
            foreach (Renderer renderer in PartRenderers(go))
            {
                if (!m_effects.ContainsKey(renderer)) m_effects[renderer] = renderer.enabled;
                renderer.enabled = false;
            }
            go.SetActive(false);
        }

        private bool IsUnclipped(Transform owner)
        {
            for (Transform node = owner; node != null; node = node.parent)
                if (m_unclippedRoots.Contains(node)) return true;
            return false;
        }

        private void ClipActor(Actor actor, bool golden)
        {
            int count = 0;
            foreach (MeshFilter filter in Parts(actor).Filters)
            {
                if (IsUnclipped(filter.transform)) continue;
                if (filter.sharedMesh == null) continue;
                ClipMesh(filter, filter.sharedMesh, golden, mesh => filter.sharedMesh = mesh);
                count++;
            }
            foreach (SkinnedMeshRenderer renderer in Parts(actor).Skins)
            {
                if (IsUnclipped(renderer.transform)) continue;
                if (renderer.sharedMesh == null) continue;
                ClipMesh(renderer, renderer.sharedMesh, golden, mesh => renderer.sharedMesh = mesh);
                count++;
            }
            // Particle/trail geometry cannot be cut as static card meshes. Keep
            // those decorative effects from spilling over the other version.
            foreach (Renderer renderer in Parts(actor).Renderers)
            {
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) continue;
                if (!m_effects.ContainsKey(renderer)) m_effects[renderer] = renderer.enabled;
                renderer.enabled = false;
            }
            if (count == 0) throw new InvalidOperationException("Card actor has no mesh to split");
        }

        private void ClipMesh(Component owner, Mesh source, bool golden, Action<Mesh> assign)
        {
            if (m_meshes.TryGetValue(owner, out MeshCopy existing) && source == existing.Clipped) return;
            Mesh clipped = MixedPremiumMesh.Clip(source,
                m_visual.transform.worldToLocalMatrix * owner.transform.localToWorldMatrix,
                m_visual.m_boxColliderCenter, m_visual.m_boxColliderSize, golden, m_upDirection, m_splitFraction);
            if (existing != null) UnityEngine.Object.Destroy(existing.Clipped);
            m_meshes[owner] = new MeshCopy { Original = source, Clipped = clipped };
            assign(clipped);
        }

        private void Fail(Exception ex)
        {
            string cardId = m_cardId;
            Cleanup();
            if (cardId != null) s_failedCards.Add(cardId);
            s_refreshPending = true;
            DebugLog.Exception("Mixed premium tile failed; restoring separate variants for " + cardId, ex);
        }

        public static void OnUpdate()
        {
            if (!s_refreshPending) return;
            s_refreshPending = false;
            if (!PreferredQualityMode.IsEnabled) return;
            try
            {
                CollectionManager.Get()?.GetCollectibleDisplay()?.GetPageManager()?.RefreshCurrentPageContents();
            }
            catch (Exception ex) { DebugLog.Exception("Mixed premium fallback refresh failed", ex); }
        }

        public static void ResetSession()
        {
            var cards = new List<MixedPremiumCard>(s_cards.Values);
            foreach (MixedPremiumCard card in cards) if (card != null) card.Cleanup();
            s_cards.Clear();
            CollapsedPremiumCount.ResetSession();
            MixedPremiumMesh.ClearReadbackCache();
            s_failedCards.Clear();
            s_refreshPending = false;
            PreferredQualityCollapse.BeginUpdate();
        }

        private void Cleanup()
        {
            foreach (var entry in m_manaPositions)
                if (entry.Key != null && entry.Key.localPosition.Equals(entry.Value.Lifted))
                    entry.Key.localPosition = entry.Value.Original;
            m_manaPositions.Clear();
            foreach (var entry in m_manaQueues)
                if (entry.Key != null) { entry.Key.RenderQueue = entry.Value; entry.Key.UpdateNow(true); }
            m_manaQueues.Clear();
            foreach (var entry in m_manaOutputs)
                if (entry.Key != null)
                {
                    entry.Key.sortingLayerID = entry.Value.Layer;
                    entry.Key.sortingOrder = entry.Value.Order;
                    entry.Key.gameObject.layer = entry.Value.GameLayer;
                }
            m_manaOutputs.Clear();
            foreach (var entry in m_actorColliders)
                if (entry.Key != null) entry.Key.enabled = entry.Value;
            m_actorColliders.Clear();
            foreach (var entry in m_stackChrome)
                if (entry.Key != null && entry.Key.localPosition.Equals(entry.Value.Lifted))
                    entry.Key.localPosition = entry.Value.Original;
            m_stackChrome.Clear();
            if (m_stacking && m_hasStackPosition && m_primary != null
                && m_primary.transform.localPosition.Equals(m_primaryStackPosition))
                m_primary.transform.localPosition = m_primaryBase;
            if (m_stacking && m_hasStackPosition && m_primary != null
                && m_primary.transform.localScale.Equals(m_primaryStackScale))
                m_primary.transform.localScale = m_primaryBaseScale;
            m_hasStackPosition = false;
            m_stacking = false;
            m_front = m_back = null;
            if (m_actors != null) s_cards.Remove(m_actors);
            m_selected = null;
            m_pressed = null;
            foreach (KeyValuePair<Component, MeshCopy> entry in m_meshes)
            {
                if (entry.Key is MeshFilter filter && filter != null && filter.sharedMesh == entry.Value.Clipped)
                    filter.sharedMesh = entry.Value.Original;
                else if (entry.Key is SkinnedMeshRenderer renderer && renderer != null && renderer.sharedMesh == entry.Value.Clipped)
                    renderer.sharedMesh = entry.Value.Original;
                UnityEngine.Object.Destroy(entry.Value.Clipped);
            }
            m_meshes.Clear();
            foreach (KeyValuePair<Renderer, bool> effect in m_effects)
                if (effect.Key != null) effect.Key.enabled = effect.Value;
            m_effects.Clear();
            foreach (var shadow in m_shadows)
                if (shadow.Key != null) shadow.Key.enabled = shadow.Value;
            m_shadows.Clear();
            foreach (KeyValuePair<GameObject, bool> text in m_hiddenObjects)
                if (text.Key != null) text.Key.SetActive(text.Value);
            m_hiddenObjects.Clear();
            foreach (var entry in m_hiddenText)
                if (entry.Key != null) { if (entry.Value) entry.Key.Hide(); else entry.Key.Show(); }
            m_hiddenText.Clear();
            m_unclippedRoots.Clear(); m_parts.Clear(); m_partRenderers.Clear();
            if (m_secondary != null) m_secondary.Destroy();
            m_secondary = null;
            m_primary = null;
            m_normal = null;
            m_golden = null;
            m_actors = null;
            enabled = false;
        }

        private void OnDestroy() => Cleanup();
    }
}
