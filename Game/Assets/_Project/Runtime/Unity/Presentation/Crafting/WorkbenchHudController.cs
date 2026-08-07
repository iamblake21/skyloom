using System;
using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using CML.Inventory;
using CML.Unity.Presentation.Inventory;
using CML.Unity.Presentation.Machines;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Unity.Presentation.Crafting
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class WorkbenchHudController : MonoBehaviour
    {
        private const int BackpackStartSlot =
            InventoryHudPresenter.HotbarSlotCount;
        private const int BackpackPreviewSlots =
            InventoryHudPresenter.PlayerSlotCount - BackpackStartSlot;

        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private CraftingCommandBridge craftingBridge;
        [SerializeField] private TransferCommandBridge transferBridge;

        private readonly InventorySlotView[] _backpackViews =
            new InventorySlotView[BackpackPreviewSlots];
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<RecipeTileView> _recipeTiles =
            new List<RecipeTileView>();

        private GameCatalog _catalog;
        private InventoryState _inventory;
        private VisualElement _screen;
        private VisualElement _backdrop;
        private VisualElement _panel;
        private VisualElement _recipeGrid;
        private ScrollView _recipeScroll;
        private VisualElement _detailIcon;
        private VisualElement _materials;
        private VisualElement _backpack;
        private Label _recipeCount;
        private Label _detailName;
        private Label _detailDescription;
        private Label _quantityLabel;
        private Label _feedback;
        private Button _minusButton;
        private Button _plusButton;
        private Button _craftButton;
        private StableId _selectedRecipeId;
        private RecipeCategory? _category;
        private bool _objectiveOnly;
        private bool _panelOpen;
        private bool _isBuilt;
        private long _craftCount = 1L;
        private int _selectedBackpackSlot = -1;

        public bool PanelOpen => _panelOpen;

        public StableId SelectedRecipeId => _selectedRecipeId;

        public void ConfigureUiAsset(
            UIDocument uiDocument,
            StyleSheet workbenchStyleSheet)
        {
            document = uiDocument;
            styleSheet = workbenchStyleSheet;
        }

        public void ConfigureCrafting(CraftingCommandBridge commandBridge)
        {
            craftingBridge = commandBridge;
            if (_isBuilt && _inventory != null && _catalog != null)
            {
                RefreshDetail();
            }
        }

        public void ConfigureInventoryCommands(TransferCommandBridge commandBridge)
        {
            transferBridge = commandBridge;
            if (_isBuilt && _inventory != null && _catalog != null)
            {
                RefreshBackpack();
            }
        }

        public void Bind(InventoryState inventory, GameCatalog catalog)
        {
            _inventory = inventory
                ?? throw new ArgumentNullException(nameof(inventory));
            _catalog = catalog
                ?? throw new ArgumentNullException(nameof(catalog));
            EnsureUi();
            Refresh();
        }

        public void SetPanelOpen(bool open)
        {
            EnsureUi();
            _panelOpen = open;
            ApplyPanelState();
            if (open)
            {
                Refresh();
            }
        }

        private void Awake()
        {
            document ??= GetComponent<UIDocument>();
            if (document != null && !document.enabled)
            {
                document.enabled = true;
            }
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            document ??= GetComponent<UIDocument>();
            if (document != null && !document.enabled)
            {
                document.enabled = true;
            }
            EnsureUi();
            ApplyPanelState();
        }

        private void Start()
        {
            EnsureUi();
            ApplyPanelState();
        }

        private void OnDisable()
        {
            _panelOpen = false;
            _isBuilt = false;
            _tabButtons.Clear();
            _screen = null;
            _backdrop = null;
            _panel = null;
            _recipeGrid = null;
            _recipeScroll = null;
            _detailIcon = null;
            _materials = null;
            _backpack = null;
            _recipeTiles.Clear();
            _selectedBackpackSlot = -1;
        }

        private void EnsureUi()
        {
            if (document == null)
            {
                return;
            }

            var root = document.rootVisualElement;
            if (root == null)
            {
                return;
            }

            if (_isBuilt
                && _screen != null
                && _panel != null
                && ReferenceEquals(
                    _screen,
                    root.Q<VisualElement>("workbench-screen"))
                && ReferenceEquals(
                    _panel,
                    root.Q<VisualElement>("workbench-panel")))
            {
                return;
            }

            // UIDocument clones a brand new visual tree after a domain reload,
            // a Play Mode recompile or a disable/enable cycle. The cached
            // elements then belong to a tree nobody renders any more, so the
            // panel has to rebind instead of trusting _isBuilt on its own.
            _isBuilt = false;
            _tabButtons.Clear();
            _recipeTiles.Clear();

            root.pickingMode = PickingMode.Ignore;
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }

            _screen = root.Q<VisualElement>("workbench-screen");
            _backdrop = root.Q<VisualElement>("workbench-backdrop");
            _panel = root.Q<VisualElement>("workbench-panel");
            _recipeGrid = root.Q<VisualElement>("workbench-recipe-grid");
            _recipeScroll = root.Q<ScrollView>("workbench-recipe-scroll");
            _detailIcon = root.Q<VisualElement>("workbench-detail-icon");
            _materials = root.Q<VisualElement>("workbench-materials");
            _backpack = root.Q<VisualElement>("workbench-backpack");
            _recipeCount = root.Q<Label>("workbench-recipe-count");
            _detailName = root.Q<Label>("workbench-detail-name");
            _detailDescription = root.Q<Label>("workbench-detail-description");
            _quantityLabel = root.Q<Label>("workbench-quantity");
            _feedback = root.Q<Label>("workbench-feedback");
            _minusButton = root.Q<Button>("workbench-minus");
            _plusButton = root.Q<Button>("workbench-plus");
            _craftButton = root.Q<Button>("workbench-craft");
            if (_screen == null || _backdrop == null || _panel == null
                || _recipeGrid == null || _recipeScroll == null
                || _detailIcon == null
                || _materials == null || _backpack == null
                || _recipeCount == null || _detailName == null
                || _detailDescription == null || _quantityLabel == null
                || _feedback == null || _minusButton == null
                || _plusButton == null || _craftButton == null)
            {
                throw new InvalidOperationException(
                    "WorkbenchHUD.uxml is missing a required named element.");
            }

            _screen.pickingMode = PickingMode.Ignore;
            _recipeScroll.mouseWheelScrollSize = 68f;
            ConfigureTab(root, "workbench-tab-objective", objective: true, null);
            ConfigureTab(root, "workbench-tab-all", objective: false, null);
            ConfigureTab(
                root,
                "workbench-tab-structures",
                objective: false,
                RecipeCategory.Structures);
            ConfigureTab(
                root,
                "workbench-tab-logistics",
                objective: false,
                RecipeCategory.Logistics);
            ConfigureTab(
                root,
                "workbench-tab-machinery",
                objective: false,
                RecipeCategory.Machinery);

            _minusButton.clicked += () => SetCraftCount(_craftCount - 1L);
            _plusButton.clicked += () => SetCraftCount(_craftCount + 1L);
            _craftButton.clicked += CraftSelected;
            _backpack.Clear();
            for (var index = 0; index < _backpackViews.Length; index++)
            {
                var slotIndex = BackpackStartSlot + index;
                _backpackViews[index] =
                    new InventorySlotView(slotIndex, hotbar: false);
                var capturedSlot = slotIndex;
                _backpackViews[index].Root.RegisterCallback<PointerDownEvent>(
                    evt => HandleBackpackPointerDown(evt, capturedSlot),
                    TrickleDown.TrickleDown);
                _backpack.Add(_backpackViews[index].Root);
            }

            _isBuilt = true;
            ApplyPanelState();
        }

        private void ConfigureTab(
            VisualElement root,
            string name,
            bool objective,
            RecipeCategory? category)
        {
            var button = root.Q<Button>(name);
            if (button == null)
            {
                throw new InvalidOperationException(
                    $"WorkbenchHUD.uxml is missing tab '{name}'.");
            }

            _tabButtons.Add(button);
            button.clicked += () =>
            {
                _objectiveOnly = objective;
                _category = category;
                _craftCount = 1L;
                for (var index = 0; index < _tabButtons.Count; index++)
                {
                    _tabButtons[index].EnableInClassList(
                        "workbench-tab--selected",
                        _tabButtons[index] == button);
                }

                Refresh();
            };
        }

        private void Refresh()
        {
            if (!_isBuilt || _inventory == null || _catalog == null)
            {
                return;
            }

            var visible = CollectVisibleRecipes();
            if (visible.Count == 0)
            {
                _selectedRecipeId = StableId.None;
            }
            else if (!Contains(visible, _selectedRecipeId))
            {
                _selectedRecipeId = visible[0].Id;
            }

            if (!RecipeTilesMatch(visible))
            {
                RebuildRecipeTiles(visible);
            }

            RefreshRecipeTiles(visible);

            _recipeCount.text = visible.Count == 1
                ? "1 ricetta"
                : $"{visible.Count} ricette";
            RefreshDetail();

            RefreshBackpack();
        }

        private bool RecipeTilesMatch(IReadOnlyList<RecipeDefinition> recipes)
        {
            if (_recipeTiles.Count != recipes.Count)
            {
                return false;
            }

            for (var index = 0; index < recipes.Count; index++)
            {
                if (_recipeTiles[index].RecipeId != recipes[index].Id)
                {
                    return false;
                }
            }

            return true;
        }

        private void RebuildRecipeTiles(
            IReadOnlyList<RecipeDefinition> recipes)
        {
            _recipeGrid.Clear();
            _recipeTiles.Clear();
            for (var index = 0; index < recipes.Count; index++)
            {
                var recipe = recipes[index];
                var presentation = CraftingHudPresenter.Project(
                    _inventory,
                    _catalog,
                    recipe);
                var tile = new Button();
                tile.AddToClassList("workbench-recipe-tile");

                var icon = new VisualElement();
                icon.AddToClassList("workbench-recipe-icon");
                icon.Add(InventorySlotView.CreateIcon(presentation.Output));
                tile.Add(icon);
                var label = new Label(presentation.DisplayName);
                label.AddToClassList("workbench-recipe-name");
                tile.Add(label);

                var capturedRecipeId = recipe.Id;
                tile.clicked += () =>
                {
                    _selectedRecipeId = capturedRecipeId;
                    _craftCount = 1L;
                    _feedback.text = string.Empty;
                    Refresh();
                };

                _recipeTiles.Add(new RecipeTileView(recipe.Id, tile));
                _recipeGrid.Add(tile);
            }

            _recipeScroll.scrollOffset = Vector2.zero;
        }

        private void RefreshRecipeTiles(
            IReadOnlyList<RecipeDefinition> recipes)
        {
            for (var index = 0; index < recipes.Count; index++)
            {
                var presentation = CraftingHudPresenter.Project(
                    _inventory,
                    _catalog,
                    recipes[index]);
                var tile = _recipeTiles[index].Root;
                tile.EnableInClassList(
                    "workbench-recipe-tile--selected",
                    recipes[index].Id == _selectedRecipeId);
                tile.EnableInClassList(
                    "workbench-recipe-tile--blocked",
                    !presentation.CanCraft);
            }
        }

        private void RefreshBackpack()
        {
            if (!_isBuilt || _inventory == null || _catalog == null)
            {
                return;
            }

            var snapshot = InventoryHudPresenter.Project(_inventory, _catalog);
            for (var index = 0; index < _backpackViews.Length; index++)
            {
                var slotIndex = BackpackStartSlot + index;
                _backpackViews[index].Bind(
                    snapshot.Slots[slotIndex],
                    slotIndex == _selectedBackpackSlot);
            }
        }

        private void HandleBackpackPointerDown(
            PointerDownEvent evt,
            int slotIndex)
        {
            if (!_panelOpen
                || evt.button != 0
                || _inventory == null)
            {
                return;
            }

            if (_selectedBackpackSlot < 0)
            {
                var source = _inventory.GetSlot(slotIndex);
                if (!source.Stack.HasValue)
                {
                    return;
                }

                _selectedBackpackSlot = slotIndex;
                _feedback.text = "Scegli lo slot di destinazione.";
                RefreshBackpack();
                evt.StopPropagation();
                return;
            }

            if (_selectedBackpackSlot == slotIndex)
            {
                _selectedBackpackSlot = -1;
                _feedback.text = string.Empty;
                RefreshBackpack();
                evt.StopPropagation();
                return;
            }

            var held = _inventory.GetSlot(_selectedBackpackSlot);
            if (!held.Stack.HasValue
                || transferBridge == null
                || !transferBridge.IsAttached)
            {
                _selectedBackpackSlot = -1;
                _feedback.text = "Riordino non collegato.";
                RefreshBackpack();
                evt.StopPropagation();
                return;
            }

            transferBridge.SubmitSlotMove(
                _inventory.InventoryId,
                _selectedBackpackSlot,
                slotIndex,
                held.Stack.Value.Quantity);
            _selectedBackpackSlot = -1;
            _feedback.text = "Zaino riordinato.";
            RefreshBackpack();
            evt.StopPropagation();
        }

        private List<RecipeDefinition> CollectVisibleRecipes()
        {
            var visible = new List<RecipeDefinition>();
            for (var index = 0; index < _catalog.Recipes.Count; index++)
            {
                var recipe = _catalog.Recipes[index];
                if (recipe.Station != CraftingStationKind.Workbench)
                {
                    continue;
                }

                if (_objectiveOnly
                    && recipe.Id != ContentIds.WorkbenchIronPlate
                    && recipe.Id != ContentIds.WorkbenchIronPickaxe)
                {
                    continue;
                }

                if (_category.HasValue && recipe.Category != _category.Value)
                {
                    continue;
                }

                visible.Add(recipe);
            }

            return visible;
        }

        private void RefreshDetail()
        {
            _detailIcon.Clear();
            _materials.Clear();
            if (_selectedRecipeId.IsNone
                || !_catalog.TryGetRecipe(_selectedRecipeId, out var recipe))
            {
                _detailName.text = "NESSUNA RICETTA";
                _detailDescription.text = string.Empty;
                _craftButton.SetEnabled(false);
                return;
            }

            var presentation = CraftingHudPresenter.Project(
                _inventory,
                _catalog,
                recipe,
                _craftCount);
            _detailName.text = presentation.DisplayName.ToUpperInvariant();
            _detailDescription.text = presentation.Description;
            _detailIcon.Add(InventorySlotView.CreateIcon(presentation.Output));
            for (var index = 0;
                 index < presentation.Ingredients.Count;
                 index++)
            {
                var ingredient = presentation.Ingredients[index];
                var row = new VisualElement();
                row.AddToClassList("workbench-material-row");
                var icon = new VisualElement();
                icon.AddToClassList("workbench-material-icon");
                icon.Add(InventorySlotView.CreateIcon(ingredient.Item));
                row.Add(icon);
                var name = new Label(ingredient.Item.DisplayName.ToUpperInvariant());
                name.AddToClassList("workbench-material-name");
                row.Add(name);
                var count = new Label($"{ingredient.Owned}/{ingredient.Required}");
                count.AddToClassList("workbench-material-count");
                count.EnableInClassList(
                    "workbench-material-count--missing",
                    !ingredient.IsAvailable);
                row.Add(count);
                _materials.Add(row);
            }

            _quantityLabel.text = _craftCount.ToString();
            _minusButton.SetEnabled(_craftCount > 1L);
            _plusButton.SetEnabled(_craftCount < 99L);
            _craftButton.text = _craftCount == 1L
                ? "CREA"
                : $"CREA ×{_craftCount}";
            _craftButton.EnableInClassList(
                "workbench-craft-button--blocked",
                !presentation.CanCraft);
            // An unavailable recipe remains clickable so the player receives the
            // precise failure message. SetEnabled(false) made the control look
            // present while silently swallowing every click.
            _craftButton.SetEnabled(
                craftingBridge != null && craftingBridge.IsAttached);
        }

        private void SetCraftCount(long value)
        {
            _craftCount = Math.Max(1L, Math.Min(99L, value));
            RefreshDetail();
        }

        private void CraftSelected()
        {
            if (_selectedRecipeId.IsNone
                || craftingBridge == null
                || !craftingBridge.IsAttached)
            {
                _feedback.text = "Crafting non collegato.";
                return;
            }

            if (!craftingBridge.TryCraft(
                    _inventory.InventoryId,
                    _selectedRecipeId,
                    CraftingStationKind.Workbench,
                    _craftCount,
                    out var failure))
            {
                _feedback.text = FailureText(failure);
                RefreshDetail();
                return;
            }

            if (craftingBridge.TryGetInventory(
                    _inventory.InventoryId,
                    out var inventory))
            {
                _inventory = inventory;
            }

            _feedback.text = "Produzione completata.";
            Refresh();
        }

        private void ApplyPanelState()
        {
            if (!_isBuilt)
            {
                return;
            }

            _backdrop.style.display = _panelOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _panel.style.display = _panelOpen
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private static bool Contains(
            IReadOnlyList<RecipeDefinition> recipes,
            StableId recipeId)
        {
            for (var index = 0; index < recipes.Count; index++)
            {
                if (recipes[index].Id == recipeId)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FailureText(CraftingFailure failure)
        {
            switch (failure)
            {
                case CraftingFailure.InsufficientIngredients:
                    return "Materiali insufficienti.";
                case CraftingFailure.InventoryFull:
                    return "Libera spazio nello zaino.";
                case CraftingFailure.AuthorityBusy:
                    return "Riprova tra un istante.";
                default:
                    return "Impossibile completare la ricetta.";
            }
        }

        private sealed class RecipeTileView
        {
            public RecipeTileView(StableId recipeId, Button root)
            {
                RecipeId = recipeId;
                Root = root ?? throw new ArgumentNullException(nameof(root));
            }

            public StableId RecipeId { get; }

            public Button Root { get; }
        }
    }
}
