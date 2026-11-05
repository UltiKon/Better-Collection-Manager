using System;
using System.Reflection;
using UnityEngine;

namespace BetterCollectionManager
{
    internal static class ComboCardTemplate
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo s_armorObject = typeof(Actor).GetField("m_armorObject", Members);
        private static readonly FieldInfo s_armorText = typeof(Actor).GetField("m_armorTextMesh", Members);
        private static readonly MethodInfo s_getArmorText = typeof(Actor).GetMethod("GetArmorText", Members, null, Type.EmptyTypes, null);
        internal static GameObject SecondaryObject(Actor actor, CategoryCatalog.ComboAppearance appearance)
            => appearance.Frame == "Hero" ? s_armorObject?.GetValue(actor) as GameObject ?? actor.m_healthObject : actor.m_healthObject;
        internal static UberText SecondaryText(Actor actor, CategoryCatalog.ComboAppearance appearance)
            => appearance.Frame == "Hero" ? s_getArmorText?.Invoke(actor, null) as UberText ?? s_armorText?.GetValue(actor) as UberText ?? actor.GetHealthText() : actor.GetHealthText();
        internal static TAG_CARDTYPE CardType(CategoryCatalog.ComboAppearance appearance)
            => (TAG_CARDTYPE)Enum.Parse(typeof(TAG_CARDTYPE), appearance.Frame.ToUpperInvariant());

        internal static EntityDef Definition(CategoryCatalog.Combo combo, EntityDef artwork)
        {
            // EntityDef.Clone copies the tag maps. Inventory definitions stay immutable.
            EntityDef definition = artwork.Clone();
            definition.SetTag(GAME_TAG.CARDTYPE, (int)CardType(combo.Appearance));
            definition.SetTag(GAME_TAG.CLASS, (int)TAG_CLASS.NEUTRAL);
            definition.SetTag(GAME_TAG.MULTIPLE_CLASSES, 0);
            definition.SetTag(GAME_TAG.HIDE_WATERMARK, 1);
            definition.SetTag(GAME_TAG.RARITY, (int)TAG_RARITY.FREE);
            definition.SetTag(GAME_TAG.ELITE, 0);
            definition.SetTag(GAME_TAG.CARDRACE, combo.Appearance.Frame == "Minion" ? (int)combo.Appearance.MinionType : 0);
            // Component artwork can have a second type; it must not leak into the combo.
            if (Enum.TryParse("CARDRACE2", out GAME_TAG secondRace)) definition.SetTag(secondRace, 0);
            definition.SetTag(GAME_TAG.SPELL_SCHOOL, combo.Appearance.Frame == "Spell" ? (int)combo.Appearance.SpellSchool : 0);
            definition.SetTag(GAME_TAG.ATK, combo.Appearance.HasAttack ? combo.Appearance.Attack : 0);
            // The client stores minion health and weapon/location durability in HEALTH.
            definition.SetTag(GAME_TAG.HEALTH, combo.Appearance.Frame == "Minion" || combo.Appearance.Frame == "Weapon"
                || combo.Appearance.Frame == "Location" ? combo.Appearance.Health : 0);
            definition.SetTag(GAME_TAG.ARMOR, combo.Appearance.Frame == "Hero" ? combo.Appearance.Health : 0);
            // A deck needs the maximum of each colour, irrespective of how
            // many copies require it. Clear all costs inherited from artwork.
            foreach (GAME_TAG tag in new[] { GAME_TAG.COST_BLOOD, GAME_TAG.COST_FROST, GAME_TAG.COST_UNHOLY })
            {
                int cost = 0;
                if (!combo.IsPackage && combo.Cards != null) foreach (var component in combo.Cards)
                {
                    var cards = ComboCollectionCards.ComponentCards(combo, component.Id);
                    EntityDef member = cards.Count > 0 ? cards[0].GetEntityDef() : null;
                    if (member != null) cost = Math.Max(cost, member.GetTag(tag));
                }
                definition.SetTag(tag, cost);
            }
            return definition;
        }
        internal static void Prepare(Actor actor, CategoryCatalog.ComboAppearance appearance)
        {
            bool attack = appearance.AttackVisible && actor.m_attackObject == null;
            bool health = appearance.SecondaryVisible && appearance.Frame != "Hero" && actor.m_healthObject == null;
            bool dragon = appearance.Dragon && actor.m_eliteObject == null;
            EntityDef definition = actor.GetEntityDef();
            bool runes = actor.m_cardRuneBanner == null && definition != null
                && (definition.GetTag(GAME_TAG.COST_BLOOD) > 0 || definition.GetTag(GAME_TAG.COST_FROST) > 0 || definition.GetTag(GAME_TAG.COST_UNHOLY) > 0);
            if (!attack && !health && !dragon && !runes) return;
            void Borrow(TAG_CARDTYPE frame, bool stats, bool ornament, TAG_PREMIUM? sourcePremium = null)
            {
                using (var template = AssetLoader.Get().LoadAsset<GameObject>(ActorNames.GetHandActor(frame, sourcePremium ?? actor.GetPremium())))
                {
                    Actor source = template?.Asset?.GetComponent<Actor>();
                    if (source == null) throw new InvalidOperationException("Native optional card elements are unavailable.");
                    GameObject Copy(GameObject part)
                    {
                        if (part == null) return null;
                        GameObject copy = UnityEngine.Object.Instantiate(part, actor.transform, false);
                        copy.transform.localPosition = source.transform.InverseTransformPoint(part.transform.position);
                        copy.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * part.transform.rotation;
                        Vector3 scale = source.transform.lossyScale, wanted = part.transform.lossyScale;
                        copy.transform.localScale = new Vector3(wanted.x / scale.x, wanted.y / scale.y, wanted.z / scale.z);
                        return copy;
                    }
                    if (stats && attack)
                    {
                        actor.m_attackObject = Copy(source.m_attackObject);
                        actor.m_attackTextMesh = actor.m_attackObject?.GetComponentInChildren<UberText>(true);
                    }
                    if (stats && health)
                    {
                        actor.m_healthObject = Copy(source.m_healthObject);
                        actor.m_healthTextMesh = actor.m_healthObject?.GetComponentInChildren<UberText>(true);
                    }
                    if (ornament && dragon) actor.m_eliteObject = Copy(source.m_eliteObject);
                    if (runes && source.m_cardRuneBanner != null)
                    {
                        actor.m_cardRuneBanner = Copy(source.m_cardRuneBanner.gameObject)?.GetComponent<CardRuneBanner>();
                        runes = actor.m_cardRuneBanner == null;
                    }
                }
            }
            TAG_CARDTYPE type = CardType(appearance);
            if (attack || health) Borrow(type, true, type == TAG_CARDTYPE.MINION);
            if (dragon && (type != TAG_CARDTYPE.MINION || (!attack && !health))) Borrow(TAG_CARDTYPE.MINION, false, true);
            if (runes) Borrow(TAG_CARDTYPE.MINION, false, false, TAG_PREMIUM.NORMAL);
        }
        internal static void SetVisible(GameObject root, bool visible)
        {
            if (root == null) return;
            root.SetActive(visible);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }
        internal static void ApplyFrame(Actor actor, CategoryCatalog.ComboAppearance appearance)
        {
            // Use the native unadorned frame, including its no-gem infill mesh.
            SetVisible(actor.m_rarityGemMesh, false);
            SetVisible(actor.m_rarityFrameMesh, false);
            SetVisible(actor.m_rarityNoGemMesh, true);
            SetVisible(actor.m_eliteObject, appearance.Dragon);
            SetVisible(actor.m_attackObject, appearance.AttackVisible);
            if (!appearance.AttackVisible && actor.GetAttackText() != null && !actor.GetAttackText().isHidden()) actor.GetAttackText().Hide();
            GameObject secondary = SecondaryObject(actor, appearance);
            if (secondary != actor.m_healthObject) SetVisible(actor.m_healthObject, false);
            GameObject armor = s_armorObject?.GetValue(actor) as GameObject;
            if (armor != secondary) SetVisible(armor, false);
            SetVisible(secondary, appearance.SecondaryVisible);
            UberText text = SecondaryText(actor, appearance);
            if (!appearance.SecondaryVisible && text != null && !text.isHidden()) text.Hide();
        }
    }
}
