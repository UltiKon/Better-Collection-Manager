using System;
using System.Reflection;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class NativeHubButtons
    {
        private static readonly MethodInfo s_textCenter = typeof(UberText).GetMethod("GetTextCenter", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo s_textRotation = typeof(UberText).GetMethod("GetTextRotation", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static UberText Caption(BoxMenuButton source)
        {
            if (source == null) return null;
            if (source.m_TextMesh != null) return source.m_TextMesh;
            foreach (UberText text in source.GetComponentsInChildren<UberText>(true)) return text;
            // Drawer buttons use a sibling ButtonName instead of wiring the
            // optional BoxMenuButton.m_TextMesh field (seen in the live client).
            Transform sibling = source.transform.parent?.Find("ButtonName");
            if (sibling != null)
                foreach (UberText text in sibling.GetComponentsInChildren<UberText>(true)) return text;
            return null;
        }

        // Configure clones under an inactive parent. Serialized references to
        // captions outside the button must also be cloned, never edited in place.
        public static BoxMenuButton Clone(BoxMenuButton source, Transform parent, string name, string caption)
        {
            UberText sourceText = Caption(source);
            if (source == null || sourceText == null) throw new InvalidOperationException("No native hub button caption (field, child or sibling ButtonName)");
            GameObject staging = new GameObject(name + "_staging");
            staging.SetActive(false);
            staging.transform.SetParent(source.transform.parent, false);
            GameObject clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(source.gameObject, staging.transform, false);
                clone.name = name;
                BoxMenuButton button = clone.GetComponent<BoxMenuButton>();
                if (button == null || button is BoxScrollButton)
                    throw new InvalidOperationException("Expected a plain native BoxMenuButton");
                button.ClearEventListeners();
                FilterButtonFeedback.BindPressSound(button);
                // Keep only the icon/hit geometry from the button clone. A
                // serialized caption can bring widget bindings, cached RTT
                // surfaces or reveal-state scripts from Collection with it.
                foreach (UberText inherited in clone.GetComponentsInChildren<UberText>(true))
                {
                    if (inherited.gameObject == clone)
                        throw new InvalidOperationException("Native caption shares the button root");
                    inherited.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(inherited.gameObject);
                }
                button.m_TextMesh = CreateCaption(sourceText, staging.transform, name + "_Caption");
                button.m_TextMesh.transform.SetParent(clone.transform, true);
                if (button.m_ButtonMeshRenderer != null && !button.m_ButtonMeshRenderer.transform.IsChildOf(clone.transform))
                    throw new InvalidOperationException("Native button artwork is outside the button");
                // A static replacement must not activate a source button's Spell
                // or external highlight. Only disable components on this clone.
                button.m_Spell = null;
                button.m_HighlightState = null;
                button.m_triggerHighlightStates = false;
                foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (Spell spell in clone.GetComponentsInChildren<Spell>(true)) spell.enabled = false;
                TooltipZone tooltip = clone.GetComponent<TooltipZone>();
                if (tooltip != null)
                {
                    tooltip.targetObject = clone;
                    tooltip.tooltipDisplayLocation = clone.transform;
                    tooltip.touchTooltipLocation = clone.transform;
                }
                button.m_TextMesh.GameStringLookup = false;
                Color color = button.m_TextMesh.TextColor;
                color.a = 1f;
                button.m_TextMesh.TextColor = color;
                button.m_TextMesh.gameObject.SetActive(true);
                button.SetText(caption);
                button.SetEnabled(true);
                clone.transform.SetParent(parent, true);
                clone.transform.position = parent.position;
                clone.SetActive(true);
                button.m_TextMesh.Show();
                button.m_TextMesh.UpdateNow(true);
                CounterTextLayout.RemoveClonedGlyphs(button.m_TextMesh);
                DebugLog.Msg($"Hub: cloned '{source.name}' using caption '{sourceText.name}', wired caption={source.m_TextMesh != null}");
                return button;
            }
            catch
            {
                if (clone != null) UnityEngine.Object.Destroy(clone);
                throw;
            }
            finally { UnityEngine.Object.Destroy(staging); }
        }

        private static UberText CreateCaption(UberText source, Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = source.gameObject.layer;
            Vector3 center = s_textCenter?.Invoke(source, null) is Vector3 offset ? offset : Vector3.zero;
            Quaternion facing = s_textRotation?.Invoke(source, null) is Quaternion rotation ? rotation : Quaternion.identity;
            go.transform.position = source.transform.TransformPoint(center);
            go.transform.rotation = source.transform.rotation * facing;
            Vector3 scale = source.transform.lossyScale, owner = parent.lossyScale;
            go.transform.localScale = new Vector3(scale.x / owner.x, scale.y / owner.y, scale.z / owner.z);
            UberText text = go.AddComponent<UberText>();
            text.TrueTypeFont = source.TrueTypeFont ?? source.GetLocalizedFont();
            text.FontSize = source.FontSize;
            text.CharacterSize = source.CharacterSize;
            text.BoldSize = source.BoldSize;
            text.Width = source.Width;
            text.Height = source.Height;
            text.Alignment = source.Alignment;
            text.Anchor = source.Anchor;
            text.TextColor = source.TextColor;
            text.GradientUpperColor = source.GradientUpperColor;
            text.GradientLowerColor = source.GradientLowerColor;
            text.RenderQueue = source.RenderQueue;
            text.WordWrap = source.WordWrap;
            text.ResizeToFit = source.ResizeToFit;
            text.ResizeToFitAndGrow = source.ResizeToFitAndGrow;
            text.RichText = source.RichText;
            text.LineSpacing = source.LineSpacing;
            text.Outline = source.Outline;
            text.OutlineSize = source.OutlineSize;
            text.OutlineColor = source.OutlineColor;
            text.GameStringLookup = false;
            text.RenderToTexture = false;
            return text;
        }
    }
}
