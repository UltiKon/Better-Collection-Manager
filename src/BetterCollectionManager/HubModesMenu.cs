using System;
using System.Collections.Generic;
using System.Reflection;
using Assets;
using HarmonyLib;
using Hearthstone.DataModels;
using Hearthstone.UI;
using UnityEngine;

namespace BetterCollectionManager
{
    internal sealed class HubModesMenu : MonoBehaviour
    {
        private static readonly FieldInfo s_records = AccessTools.Field(typeof(GameModeDisplay), "m_activeGameModeRecords");
        private static readonly FieldInfo s_selected = AccessTools.Field(typeof(GameModeDisplay), "m_selectedGameModeButtonDataModel");
        private static readonly FieldInfo s_back = AccessTools.Field(typeof(GameModeDisplay), "m_backButton");
        private static readonly MethodInfo s_canEnter = AccessTools.Method(typeof(GameModeDisplay), "CanEnterMode");
        private GameModeDisplay m_display;
        private UIBButton m_fallback;
        private UIBButton m_backTemplate;
        private readonly MaterialPropertyBlock m_artProperties = new MaterialPropertyBlock();
        private bool m_ready;
        private bool m_layoutLogged;
        private bool m_renderableLogged;

        internal void Initialize(GameModeDisplay display)
        {
            m_display = display;
            m_ready = false;
            m_layoutLogged = false;
            m_renderableLogged = false;
            if (m_fallback != null) UnityEngine.Object.Destroy(m_fallback.gameObject);
            m_fallback = null;
        }

        private void LateUpdate() { Refresh(); }

        internal void Refresh()
        {
            if (!HubMenuPatches.SwapEnabled || m_display == null) return;
            try
            {
                if (!m_ready && m_display.IsFinishedLoading) Setup();
                if (m_fallback != null)
                {
                    bool placed = FollowBackButton();
                    m_fallback.SetEnabled(placed && CanOpenBattlegrounds());
                }
            }
            catch (Exception ex)
            {
                DebugLog.Exception("Hub: Modes replacement failed", ex);
                if (m_fallback != null) UnityEngine.Object.Destroy(m_fallback.gameObject);
                m_fallback = null;
                HubMenuPatches.DisableSwap("Modes access could not be prepared");
            }
        }

        private void Setup()
        {
            GameModeSceneDataModel scene = m_display.GetGameModeSceneDataModel();
            var records = s_records?.GetValue(m_display) as List<GameModeDbfRecord>;
            if (scene?.GameModeButtons == null || records == null || s_selected == null || s_canEnter == null)
                throw new InvalidOperationException("Modes data/entry API unavailable");
            GameModeDbfRecord legacy = null;
            foreach (GameModeDbfRecord record in GameDbf.GameMode.GetRecords())
            {
                DebugLog.Msg($"Hub Modes DBF: id={record.ID}, scene='{record.LinkedScene}', state='{record.GameModeButtonState}'");
                if (record.LinkedScene == "BACON" && !string.IsNullOrEmpty(record.GameModeButtonState))
                    legacy = record;
            }

            bool hasVisual = false;
            if (legacy != null)
                foreach (VisualController controller in m_display.GetComponentsInChildren<VisualController>(true))
                    if (controller.HasState(legacy.GameModeButtonState)) { hasVisual = true; break; }

            // Prepare guaranteed access before removing any entry. A retained
            // DBF row is not sufficient evidence that its old widget art exists.
            if (!hasVisual) CreateFallback();

            var buttons = new DataModelList<GameModeButtonDataModel>();
            var active = new List<GameModeDbfRecord>();
            foreach (GameModeButtonDataModel button in scene.GameModeButtons)
            {
                GameModeDbfRecord record = GameDbf.GameMode.GetRecord(button.GameModeRecordId);
                if (record != null && (record.LinkedScene == "TAVERN_BRAWL" || record.LinkedScene == "BACON")) continue;
                buttons.Add(button);
            }
            foreach (GameModeDbfRecord record in records)
                if (record.LinkedScene != "TAVERN_BRAWL" && record.LinkedScene != "BACON") active.Add(record);
            if (hasVisual)
            {
                active.Add(legacy);
                buttons.Add(new GameModeButtonDataModel
                {
                    GameModeRecordId = legacy.ID,
                    Name = legacy.Name,
                    Description = legacy.Description,
                    ButtonState = legacy.GameModeButtonState,
                    ModuleTag = Hearthstone.Core.Streaming.DownloadTags.GetTagString(
                        Hearthstone.Core.Streaming.DownloadTags.Content.Bgs)
                });
            }
            // No global DBF/Event/feature mutation: only this scene's list.
            s_records.SetValue(m_display, active);
            var selected = s_selected.GetValue(m_display) as GameModeButtonDataModel;
            bool selectionSurvives = false;
            foreach (GameModeButtonDataModel button in buttons)
                if (button.GameModeRecordId == scene.LastSelectedGameModeRecordId) selectionSurvives = true;
            if (!selectionSurvives)
            {
                scene.LastSelectedGameModeRecordId = buttons.Count > 0 ? buttons[0].GameModeRecordId : 0;
                s_selected.SetValue(m_display, null);
            }
            else if (selected != null)
            {
                bool selectedSurvives = false;
                foreach (GameModeButtonDataModel button in buttons)
                    if (button.GameModeRecordId == selected.GameModeRecordId) selectedSurvives = true;
                if (!selectedSurvives) s_selected.SetValue(m_display, null);
            }
            scene.GameModeButtons = buttons;
            m_ready = true;
            DebugLog.Msg($"Hub: Battlegrounds Modes record={(legacy == null ? "absent" : legacy.ID.ToString())}, " +
                $"state='{legacy?.GameModeButtonState}', retained widget state={hasVisual}; " +
                (hasVisual ? "reusing native tile" : "using native text button beside Back"));
        }

        private void CreateFallback()
        {
            var source = s_back?.GetValue(m_display) as UIBButton;
            if (source == null || source.m_ButtonText == null)
                throw new InvalidOperationException("Modes Back button unavailable");
            GameObject staging = new GameObject("BCM_BattlegroundsModes_staging");
            staging.SetActive(false);
            staging.transform.SetParent(source.transform.parent, false);
            GameObject clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(source.gameObject, staging.transform, false);
                clone.name = "BCM_BattlegroundsModes";
                UIBButton button = clone.GetComponent<UIBButton>();
                if (button == null || button.m_ButtonText == null || !button.m_ButtonText.transform.IsChildOf(clone.transform)
                    || (button.m_RootObject != null && !button.m_RootObject.transform.IsChildOf(clone.transform)))
                    throw new InvalidOperationException("Modes Back button has external artwork/text");
                button.BubbleUpEvent = string.Empty;
                button.m_AnimateFlip = false;
                button.m_WiggleTime = 0f;
                button.m_DepressTime = 0f;
                button.m_RaiseTime = 0f;
                button.m_ButtonText.GameStringLookup = false;
                button.m_ButtonText.gameObject.SetActive(true);
                button.SetText(GameStrings.Get("GLUE_BACON"));
                FilterButtonFeedback.BindPressSound(button);
                button.AddEventListener(UIEventType.RELEASE, _ => OpenBattlegrounds());
                var hit = source.GetComponent<Collider>();
                if (hit == null) throw new InvalidOperationException("Modes button hit geometry unavailable");
                clone.transform.SetParent(source.transform.parent, true);
                clone.SetActive(true);
                button.m_ButtonText.Show();
                button.m_ButtonText.UpdateNow(true);
                CounterTextLayout.RemoveClonedGlyphs(button.m_ButtonText);
                m_fallback = button;
                m_backTemplate = source;
            }
            catch
            {
                if (clone != null) UnityEngine.Object.Destroy(clone);
                throw;
            }
            finally { UnityEngine.Object.Destroy(staging); }
        }

        private bool FollowBackButton()
        {
            if (m_backTemplate == null || m_fallback == null) return false;
            bool visible = m_backTemplate.gameObject.activeInHierarchy;
            m_fallback.gameObject.SetActive(visible);
            if (!visible) { m_fallback.m_ButtonText.Hide(); return false; }

            // The ready callback runs before the native widget finishes moving
            // and revealing Back. Follow its current pose and artwork state;
            // a one-time clone otherwise remains at its hidden/loading pose.
            Transform source = m_backTemplate.transform, target = m_fallback.transform;
            target.rotation = source.rotation;
            Vector3 scale = source.localScale;
            scale.x *= 1.8f;
            target.localScale = scale;
            target.position = source.position;
            FollowArtwork(source, target, true);

            Camera camera = CameraUtils.FindProjectionCameraForObject(m_backTemplate.gameObject);
            if (camera == null || !HitRect(m_backTemplate, camera, out Rect back)
                || !HitRect(m_fallback, camera, out Rect button))
            { m_fallback.gameObject.SetActive(false); m_fallback.m_ButtonText.Hide(); return false; }
            Rect viewport = camera.pixelRect;
            if (back.xMax <= viewport.xMin || back.xMin >= viewport.xMax
                || back.yMax <= viewport.yMin || back.yMin >= viewport.yMax)
            { m_fallback.gameObject.SetActive(false); m_fallback.m_ButtonText.Hide(); return false; }
            float gap = Math.Max(8f, back.width * .15f);
            float x = back.xMin - gap - button.width * .5f;
            x = Math.Max(viewport.xMin + 8f + button.width * .5f,
                Math.Min(viewport.xMax - 8f - button.width * .5f, x));
            float y = back.center.y;
            if (x + button.width * .5f > back.xMin - gap)
                y = back.yMax + gap + button.height * .5f;
            y = Math.Max(viewport.yMin + 8f + button.height * .5f,
                Math.Min(viewport.yMax - 8f - button.height * .5f, y));
            Vector3 from = camera.WorldToScreenPoint(target.position), to = from;
            to.x += x - button.center.x;
            to.y += y - button.center.y;
            target.position += camera.ScreenToWorldPoint(to) - camera.ScreenToWorldPoint(from);
            m_fallback.m_ButtonText.TextColor = m_backTemplate.m_ButtonText.TextColor;
            m_fallback.m_ButtonText.gameObject.layer = m_backTemplate.m_ButtonText.gameObject.layer;
            if (m_backTemplate.m_ButtonText.isHidden()) m_fallback.m_ButtonText.Hide();
            else m_fallback.m_ButtonText.Show();
            int renderers = 0;
            foreach (Renderer renderer in m_fallback.GetComponentsInChildren<Renderer>(false))
                if (renderer.enabled) renderers++;
            bool renderable = renderers > 0 && !m_fallback.m_ButtonText.isHidden();
            if (!m_layoutLogged || (!m_renderableLogged && renderable))
            {
                HitRect(m_fallback, camera, out Rect placed);
                DebugLog.Msg($"Hub: Battlegrounds fallback layout camera='{camera.name}', Back={back}, button={placed}, " +
                    $"active={m_fallback.gameObject.activeInHierarchy}, renderers={renderers}, captionHidden={m_fallback.m_ButtonText.isHidden()}, " +
                    $"layer={m_fallback.gameObject.layer}, cameraMask={camera.cullingMask}");
                m_layoutLogged = true;
                m_renderableLogged |= renderable;
            }
            return true;
        }

        private void FollowArtwork(Transform source, Transform target, bool root = false)
        {
            target.gameObject.layer = source.gameObject.layer;
            if (!root)
            {
                target.localPosition = source.localPosition;
                target.localRotation = source.localRotation;
                target.localScale = source.localScale;
                target.gameObject.SetActive(source.gameObject.activeSelf);
            }
            Renderer original = source.GetComponent<Renderer>(), copy = target.GetComponent<Renderer>();
            if (original != null && copy != null)
            {
                copy.enabled = original.enabled;
                original.GetPropertyBlock(m_artProperties);
                copy.SetPropertyBlock(m_artProperties);
            }
            for (int i = 0; i < source.childCount; i++)
            {
                Transform child = source.GetChild(i);
                if (child.GetComponent<UberText>() != null) continue;
                Transform counterpart = target.Find(child.name);
                if (counterpart != null) FollowArtwork(child, counterpart);
            }
        }

        private static bool HitRect(PegUIElement button, Camera camera, out Rect rect)
        {
            rect = default;
            Collider hit = button.GetComponent<Collider>();
            if (hit == null || hit.bounds.size.sqrMagnitude <= .000001f) return false;
            Bounds bounds = hit.bounds;
            float left = float.PositiveInfinity, right = float.NegativeInfinity;
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            {
                Vector3 p = camera.WorldToScreenPoint(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                    y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z));
                if (p.z <= 0f) return false;
                left = Math.Min(left, p.x); right = Math.Max(right, p.x);
                bottom = Math.Min(bottom, p.y); top = Math.Max(top, p.y);
            }
            rect = Rect.MinMaxRect(left, bottom, right, top);
            return rect.width > 0f && rect.height > 0f;
        }

        internal bool TryNavigateBattlegrounds()
        {
            if (!m_ready || s_selected == null || s_canEnter == null) return false;
            var selected = s_selected.GetValue(m_display) as GameModeButtonDataModel;
            if (selected == null || GameDbf.GameMode.GetRecord(selected.GameModeRecordId)?.LinkedScene != "BACON") return false;
            // Let native locked/download handling display its own explanations.
            object[] args = { string.Empty, GameModeDisplay.ModeLockedStatus.Locked };
            if (!(bool)s_canEnter.Invoke(m_display, args)) return false;
            OpenBattlegrounds();
            return true;
        }

        private static bool CanOpenBattlegrounds()
        {
            Box box = Box.Get();
            if (box == null || box.m_BattleGroundsButton == null || SceneMgr.Get() == null
                || SceneMgr.Get().GetMode() != SceneMgr.Mode.GAME_MODE || !HubMenuPatches.CanInteract(box)) return false;
            var features = NetCache.Get()?.GetNetObject<NetCache.NetCacheFeatures>();
            return features?.Games != null && features.Games.GetFeatureFlag(box.m_BattleGroundsButton.m_feature)
                && GameModeUtils.HasUnlockedMode(Global.UnlockableGameMode.BATTLEGROUNDS);
        }

        internal void OpenBattlegrounds()
        {
            if (!CanOpenBattlegrounds()) return;
            Box box = Box.Get();
            box.OnBattleGroundsButtonPressed(new UIEvent(UIEventType.RELEASE, box.m_BattleGroundsButton));
        }

        private void OnDestroy()
        {
            if (m_fallback != null) UnityEngine.Object.Destroy(m_fallback.gameObject);
        }
    }
}
