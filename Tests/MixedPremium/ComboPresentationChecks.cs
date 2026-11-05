using System;
using System.Linq;
using BetterCollectionManager;
using UnityEngine;

internal static partial class Program
{
    private static void CheckComboPresentation(CollectibleRenderFixture renderer, CollectibleCard proxy,
        CollectibleCard carrier, CategoryCatalog.Combo combo)
    {
        CollectionManager manager = CollectionManager.Get();
        var previousCards = manager.Cards.ToArray(); var previousCounts = manager.Counts.ToArray();
        var previousOrder = PreferredPremiumPrefs.Order;
        float previousTime = Time.unscaledTime;
        var source = AssetLoader.Get().InstantiatePrefab("NORMAL", Assets.AssetLoadingOptions.IgnorePrefabPosition).GetComponent<Actor>();
        source.SetEntityDef(carrier.GetEntityDef());
        var cached = new CollectionCardActors(source);
        var visual = new GameObject("ComboPresentation").AddComponent<CollectionCardVisual>();
        try
        {
            PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.NORMAL };
            string first = ComboCollectionCards.ComponentCards(combo, combo.Cards[0].Id)[0].CardId;
            string second = ComboCollectionCards.ComponentCards(combo, combo.Cards[1].Id)[0].CardId;
            void Variant(string id, TAG_PREMIUM premium, int count)
            {
                CollectibleCard normal = manager.Cards[(id, TAG_PREMIUM.NORMAL)];
                var card = Card(id, premium, count); card.ManaCost = normal.ManaCost;
                manager.Cards[(id, premium)] = card; manager.Counts[(id, premium)] = count;
            }
            Variant(first, TAG_PREMIUM.NORMAL, 2); Variant(second, TAG_PREMIUM.NORMAL, 2);
            Variant(first, TAG_PREMIUM.GOLDEN, 1); Variant(first, TAG_PREMIUM.DIAMOND, 0);
            Variant(second, TAG_PREMIUM.SIGNATURE, 1);
            Variant(second, TAG_PREMIUM.GOLDEN, 1);
            Mesh nativePortrait = null;
            var nativeFactory = AssetLoader.Get().MeshFactory;
            int instances = AssetLoader.Get().InstantiateCalls;
            try
            {
                AssetLoader.Get().MeshFactory = () => {
                    Mesh mesh = nativePortrait = nativeFactory();
                    // Real premium art need not have one affine UV basis.
                    mesh.SetUVs(0, mesh.vertices.Select((_, index) => new Vector4(index % 2 == 0 ? .13f : .91f, index % 3 * .4f, 0, 0)).ToList());
                    return mesh;
                };
                renderer.Render(proxy, visual, cached);
            }
            finally { AssetLoader.Get().MeshFactory = nativeFactory; }
            Actor actor = visual.GetActor();
            Check(actor.GetPremium() == TAG_PREMIUM.GOLDEN && actor.gameObject.name == "GOLDEN" && ComboCollectionCards.ArtworkPremium(actor) == TAG_PREMIUM.GOLDEN
                && source.GetPremium() == TAG_PREMIUM.NORMAL,
                "The combo uses the highest owned common template: Golden even when one component also owns Signature");
            var uv = new System.Collections.Generic.List<Vector4>(); nativePortrait.GetUVs(0, uv);
            Check(actor.GetComponent<MeshFilter>().sharedMesh == nativePortrait
                && AssetLoader.Get().InstantiateCalls == instances + 1
                && uv.Select((value, index) => value.x == (index % 2 == 0 ? .13f : .91f) && value.y == index % 3 * .4f).All(value => value),
                "Native premium portrait coordinates are used unchanged, without an offscreen donor or a second crop inferred from a source triangle");
            Check(actor.NameText.RichText && actor.CostText.RichText && actor.RulesText.RichText
                && actor.NameText.ResizeToFit && !actor.NameText.ResizeToFitAndGrow && !actor.NameText.WordWrap
                && actor.RulesText.ResizeToFit && !actor.RulesText.ResizeToFitAndGrow && actor.RulesText.WordWrap,
                "Combo labels retain native material-tag parsing and fit titles and wrapped descriptions inside their native text areas");

            string cost = actor.CostText.Text;
            actor.CostText.Width = 0;
            actor.CostText.ResizeToFit = actor.CostText.ResizeToFitAndGrow = true;
            actor.CostText.Hide(); actor.CostText.gameObject.SetActive(false); actor.m_manaObject.SetActive(false);
            ComboCollectionCards.OnLateUpdate();
            Check(!actor.CostText.ResizeToFit && !actor.CostText.ResizeToFitAndGrow && !actor.CostText.isHidden()
                && actor.CostText.gameObject.activeInHierarchy && actor.m_manaObject.activeSelf
                && actor.CostText.GetTextWorldSpaceBounds().size.x > 0 && CounterTextLayout.VisibleRenderers(actor.CostText).Length > 0,
                "Mana stays visibly rendered at native digit size even on a zero-width widget with hidden text and inactive mana objects");
            float nameWidth = actor.NameText.Width, rulesWidth = actor.RulesText.Width;
            // Late native components can replace captions after SetActors/Show,
            // including material markup and the component's own cost and rules.
            actor.NameText.Text = "<material=1></material>Consecration";
            actor.RulesText.Text = "<material=1></material>Deal 2 damage to all enemies.";
            actor.CostText.Text = "3";
            actor.RulesText.WordWrap = false; actor.NameText.ResizeToFit = false;
            actor.CostText.Hide();
            actor.m_rarityGemMesh.SetActive(true);
            ComboCollectionCards.OnLateUpdate();
            Check(actor.NameText.Text == combo.Name && actor.RulesText.Text == combo.Appearance.CardText && actor.CostText.Text == cost
                && actor.NameText.RichText && actor.RulesText.RichText && actor.NameText.ResizeToFit && actor.RulesText.WordWrap
                && !actor.CostText.isHidden() && !actor.CostText.ResizeToFit
                && !actor.m_rarityGemMesh.activeSelf && actor.NameText.Width == nameWidth && actor.RulesText.Width == rulesWidth,
                "A late native card-text refresh cannot replace combo metadata, reveal markup or rarity, or enlarge its text regions");
            int updates = actor.NameText.UpdateCalls + actor.CostText.UpdateCalls + actor.RulesText.UpdateCalls;
            int scans = manager.GetAllCardsCalls;
            for (int i = 0; i < 20; i++) ComboCollectionCards.OnLateUpdate();
            Check(updates == actor.NameText.UpdateCalls + actor.CostText.UpdateCalls + actor.RulesText.UpdateCalls
                && scans == manager.GetAllCardsCalls,
                "Stable combo frames neither rebuild text geometry nor rescan the inventory to calculate mana");

            string originalRules = combo.Appearance.CardText;
            var keywordTable = GameDbf.KeywordText;
            try
            {
                GameDbf.KeywordText = new KeywordTable();
                GameDbf.KeywordText.Records.Add(new KeywordTextDbfRecord { Tag = 190 });
                GameStrings.Names[(GAME_TAG)190] = "Taunt";
                combo.Appearance.CardText = "Gain Taunt. <b>Taunt</b>";
                ComboCollectionCards.OnLateUpdate();
                Check(actor.RulesText.RichText && actor.RulesText.Text == "Gain <b>Taunt</b>. <b>Taunt</b>"
                    && combo.Appearance.CardText == "Gain Taunt. <b>Taunt</b>",
                    "Combo actors render automatically bold keywords without changing editable saved text");
                int rulesUpdates = actor.RulesText.UpdateCalls;
                ComboCollectionCards.OnLateUpdate();
                Check(actor.RulesText.UpdateCalls == rulesUpdates, "Stable formatted captions do not rebuild native text each frame");
                actor.RulesText.Text = "Native text refreshed";
                ComboCollectionCards.OnLateUpdate();
                Check(actor.RulesText.Text == "Gain <b>Taunt</b>. <b>Taunt</b>", "Late native updates retain automatic keyword formatting");
            }
            finally
            {
                combo.Appearance.CardText = originalRules;
                GameStrings.Names.Remove((GAME_TAG)190); GameDbf.KeywordText = keywordTable;
                ComboCollectionCards.OnLateUpdate();
            }

            Time.unscaledTime += ComboCollectionCards.ArtworkInterval + .01f;
            ComboCollectionCards.OnUpdate(); actor = visual.GetActor();
            Check(actor.GetPremium() == TAG_PREMIUM.GOLDEN && actor.gameObject.name == "GOLDEN" && ComboCollectionCards.ArtworkPremium(actor) == TAG_PREMIUM.GOLDEN
                && actor.GetEntityDef().GetCardId() == second && actor.NameText.Text == combo.Name && actor.CostText.Text == cost,
                "Artwork rotation changes the component identity while retaining the common Golden native frame and portrait quality");
            ComboPopup.Inspect(combo);
            Actor[] previews = ButtonListMenu.Visible.GetComponentsInChildren<Actor>(false);
            Check(previews.Single(a => a.GetEntityDef().GetCardId() == first).GetPremium() == TAG_PREMIUM.GOLDEN
                && previews.Single(a => a.GetEntityDef().GetCardId() == second).GetPremium() == TAG_PREMIUM.SIGNATURE,
                "Large component previews retain their individually preferred owned qualities");
            ComboPopup.Close();

            manager.Counts[(second, TAG_PREMIUM.GOLDEN)] = 0;
            Check(ComboCollectionCards.CommonPremium(combo) == TAG_PREMIUM.NORMAL,
                "A Golden catalog variant with zero ownership cannot upgrade a combo containing only owned Normal and Signature variants");
            Variant(first, TAG_PREMIUM.SIGNATURE, 1);
            Check(ComboCollectionCards.CommonPremium(combo) == TAG_PREMIUM.SIGNATURE,
                "Signature becomes the common template only when every component owns Signature");
            renderer.Render(proxy, visual, cached);
            Check(visual.GetActor().GetPremium() == TAG_PREMIUM.SIGNATURE && ActorNames.LastTemplateIdentity == first,
                "A shared Signature template supplies the first component's frame identity instead of falling back to a Golden asset");
            Time.unscaledTime += ComboCollectionCards.ArtworkInterval + .01f;
            ComboCollectionCards.OnUpdate();
            Check(visual.GetActor().GetEntityDef().GetCardId() == second && visual.GetActor().GetPremium() == TAG_PREMIUM.SIGNATURE
                && ActorNames.LastTemplateIdentity == first,
                "Signature artwork rotation retains the originally selected frame identity across component sets");
            manager.Counts[(first, TAG_PREMIUM.SIGNATURE)] = 0;
            manager.Counts[(second, TAG_PREMIUM.SIGNATURE)] = 0;
            Variant(first, TAG_PREMIUM.DIAMOND, 1); Variant(second, TAG_PREMIUM.DIAMOND, 1);
            Check(ComboCollectionCards.CommonPremium(combo) == TAG_PREMIUM.DIAMOND,
                "Diamond is chosen ahead of Golden when both component cards own it");

            Variant(second, TAG_PREMIUM.GOLDEN, 1); Variant(second, TAG_PREMIUM.SIGNATURE, 1);

            PreferredPremiumPrefs.Order = new[] { TAG_PREMIUM.NORMAL, TAG_PREMIUM.GOLDEN, TAG_PREMIUM.SIGNATURE, TAG_PREMIUM.DIAMOND };
            renderer.Render(proxy, visual, cached);
            Check(visual.GetActor().GetPremium() == TAG_PREMIUM.DIAMOND && ComboCollectionCards.ArtworkPremium(visual.GetActor()) == TAG_PREMIUM.DIAMOND,
                "The combo template always uses the highest common owned quality, independently of individual card display preferences");
            ComboPopup.Inspect(combo);
            Check(ButtonListMenu.Visible.GetComponentsInChildren<Actor>(false).All(a => a.GetPremium() == TAG_PREMIUM.NORMAL),
                "Combo inspect honors the saved quality order too");
            ComboPopup.Close();

            // Reuse a caption-bearing tile to verify that fitting overrides are
            // reversible, including the game's original rich-text configuration.
            renderer.Render(carrier, visual, cached);
            source.NameText.RichText = true; source.NameText.GameStringLookup = true;
            source.NameText.ResizeToFit = false; source.NameText.ResizeToFitAndGrow = true; source.NameText.WordWrap = true;
            source.NameText.Text = "<material=1>Native name</material>";
            source.CostText.ResizeToFit = true; source.CostText.Hide();
            source.CostText.gameObject.SetActive(false); source.m_manaObject.SetActive(false);
            ComboCollectionCards.CaptureBinding(visual, new object[] { proxy });
            ComboCollectionCards.Apply(visual); ComboCollectionCards.Forget(visual);
            Check(source.NameText.Text == "<material=1>Native name</material>" && source.NameText.RichText && source.NameText.GameStringLookup
                && !source.NameText.ResizeToFit && source.NameText.ResizeToFitAndGrow && source.NameText.WordWrap,
                "Reusing a tile restores its native caption, markup parsing, localization and all original fitting settings");
            Check(source.CostText.isHidden() && source.CostText.ResizeToFit
                && !source.CostText.gameObject.activeSelf && !source.m_manaObject.activeSelf,
                "Releasing combo mana presentation restores the native digit fitting and both text and object visibility");
        }
        finally
        {
            ComboPopup.Close(); ComboCollectionCards.ReleaseHiddenActors(visual); ComboCollectionCards.Forget(visual);
            source.Destroy(); UnityEngine.Object.Destroy(visual.gameObject);
            manager.Cards.Clear(); manager.Counts.Clear();
            foreach (var pair in previousCards) manager.Cards.Add(pair.Key, pair.Value);
            foreach (var pair in previousCounts) manager.Counts.Add(pair.Key, pair.Value);
            PreferredPremiumPrefs.Order = previousOrder; Time.unscaledTime = previousTime;
        }
    }
}
