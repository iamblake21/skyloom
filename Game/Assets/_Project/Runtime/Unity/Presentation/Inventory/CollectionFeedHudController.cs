using System.Collections.Generic;
using CML.Content;
using CML.Foundation;
using UnityEngine;
using UnityEngine.UIElements;

namespace CML.Unity.Presentation.Inventory
{
    /// <summary>
    /// Read-only collection feedback. Entries are created only after gameplay
    /// has committed an inventory successor; this component never awards or
    /// moves items itself.
    /// </summary>
    [DefaultExecutionOrder(340)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CollectionFeedHudController : MonoBehaviour
    {
        private const float EnterDuration = 0.14f;
        private const float HoldDuration = 3.40f;
        private const float ExitDuration = 0.30f;
        private const float PopDuration = 0.18f;
        private const float EnterDistance = 74f;
        private const float ExitDistance = 28f;

        [SerializeField] private UIDocument document;

        private readonly Dictionary<StableId, FeedEntry> _entries =
            new Dictionary<StableId, FeedEntry>();
        private readonly List<StableId> _removalBuffer =
            new List<StableId>();
        private VisualElement _feed;

        public static CollectionFeedHudController EnsureFor(
            InventoryHudController inventoryHud)
        {
            if (inventoryHud == null)
            {
                return null;
            }

            var controller =
                inventoryHud.GetComponent<CollectionFeedHudController>();
            if (controller == null)
            {
                controller =
                    inventoryHud.gameObject.AddComponent<
                        CollectionFeedHudController>();
            }

            return controller;
        }

        public void ShowCommittedCollection(
            StableId itemId,
            long quantity,
            GameCatalog catalog)
        {
            if (itemId.IsNone
                || quantity <= 0L
                || catalog == null
                || !catalog.TryGetItem(itemId, out var definition))
            {
                return;
            }

            EnsureUi();
            if (_feed == null)
            {
                return;
            }

            if (_entries.TryGetValue(itemId, out var existing))
            {
                existing.Quantity += quantity;
                existing.Label.text = FormatLabel(
                    existing.DisplayName,
                    existing.Quantity);
                existing.Phase = FeedPhase.Holding;
                existing.PhaseTime = 0f;
                existing.PopTime = 0f;
                existing.Root.style.opacity = 1f;
                SetPosition(existing.Root, 0f, 0f);
                existing.Root.BringToFront();
                return;
            }

            var presentation = InventoryHudPresenter.ProjectSlot(
                0,
                itemId,
                quantity,
                definition);
            var entry = CreateEntry(presentation, quantity);
            _entries.Add(itemId, entry);
            _feed.Add(entry.Root);
        }

        private void Awake()
        {
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }
        }

        private void OnEnable()
        {
            EnsureUi();
        }

        private void Update()
        {
            if (_entries.Count == 0)
            {
                return;
            }

            var deltaTime = Time.unscaledDeltaTime;
            _removalBuffer.Clear();
            foreach (var pair in _entries)
            {
                UpdateEntry(pair.Value, deltaTime);
                if (pair.Value.Phase == FeedPhase.Finished)
                {
                    _removalBuffer.Add(pair.Key);
                }
            }

            for (var index = 0;
                 index < _removalBuffer.Count;
                 index++)
            {
                var itemId = _removalBuffer[index];
                if (!_entries.TryGetValue(itemId, out var entry))
                {
                    continue;
                }

                entry.Root.RemoveFromHierarchy();
                _entries.Remove(itemId);
            }
        }

        private void EnsureUi()
        {
            if (document == null)
            {
                document = GetComponent<UIDocument>();
            }

            var documentRoot = document?.rootVisualElement;
            if (documentRoot == null)
            {
                return;
            }

            if (_feed != null && _feed.panel == documentRoot.panel)
            {
                _feed.BringToFront();
                return;
            }

            _feed = new VisualElement
            {
                name = "collection-feed",
                pickingMode = PickingMode.Ignore
            };
            _feed.style.position = Position.Absolute;
            _feed.style.right = 24f;
            _feed.style.top = Length.Percent(35f);
            _feed.style.width = 250f;
            _feed.style.alignItems = Align.FlexEnd;
            _feed.style.flexDirection = FlexDirection.Column;
            documentRoot.Add(_feed);
            _feed.BringToFront();
        }

        private static FeedEntry CreateEntry(
            InventorySlotPresentation presentation,
            long quantity)
        {
            var root = new VisualElement
            {
                name = $"collection-feed-{presentation.ItemId}",
                pickingMode = PickingMode.Ignore
            };
            root.style.height = 34f;
            root.style.minWidth = 126f;
            root.style.maxWidth = 218f;
            root.style.marginBottom = 6f;
            root.style.paddingLeft = 7f;
            root.style.paddingRight = 11f;
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.FlexStart;
            root.style.backgroundColor =
                new Color(1f, 1f, 1f, 0.18f);
            root.style.borderTopLeftRadius = 10f;
            root.style.borderTopRightRadius = 10f;
            root.style.borderBottomLeftRadius = 10f;
            root.style.borderBottomRightRadius = 10f;

            var iconHost = new VisualElement
            {
                name = "collection-feed-icon",
                pickingMode = PickingMode.Ignore
            };
            iconHost.style.width = 23f;
            iconHost.style.height = 23f;
            iconHost.style.marginRight = 7f;
            iconHost.style.flexShrink = 0f;
            iconHost.style.alignItems = Align.Center;
            iconHost.style.justifyContent = Justify.Center;
            var icon = InventorySlotView.CreateIcon(presentation);
            icon.pickingMode = PickingMode.Ignore;
            iconHost.Add(icon);
            root.Add(iconHost);

            var label = new Label
            {
                name = "collection-feed-label",
                text = FormatLabel(presentation.DisplayName, quantity),
                pickingMode = PickingMode.Ignore
            };
            label.style.color = new Color(0.98f, 0.94f, 0.85f, 0.94f);
            label.style.fontSize = 11f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 0.7f;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            root.Add(label);

            root.style.opacity = 0f;
            SetPosition(root, EnterDistance, 0f);

            return new FeedEntry(
                root,
                label,
                presentation.DisplayName,
                quantity);
        }

        private static void UpdateEntry(
            FeedEntry entry,
            float deltaTime)
        {
            entry.PhaseTime += deltaTime;

            switch (entry.Phase)
            {
                case FeedPhase.Entering:
                {
                    var t = Mathf.Clamp01(
                        entry.PhaseTime / EnterDuration);
                    var eased = EaseOutCubic(t);
                    SetPosition(
                        entry.Root,
                        Mathf.Lerp(EnterDistance, 0f, eased),
                        0f);
                    entry.Root.style.opacity = eased;
                    if (t >= 1f)
                    {
                        entry.Phase = FeedPhase.Holding;
                        entry.PhaseTime = 0f;
                    }

                    break;
                }

                case FeedPhase.Holding:
                    SetPosition(entry.Root, 0f, 0f);
                    entry.Root.style.opacity = 1f;
                    if (entry.PhaseTime >= HoldDuration)
                    {
                        entry.Phase = FeedPhase.Exiting;
                        entry.PhaseTime = 0f;
                    }

                    break;

                case FeedPhase.Exiting:
                {
                    var t = Mathf.Clamp01(
                        entry.PhaseTime / ExitDuration);
                    var eased = t * t;
                    SetPosition(
                        entry.Root,
                        0f,
                        Mathf.Lerp(0f, ExitDistance, eased));
                    entry.Root.style.opacity = 1f - eased;
                    if (t >= 1f)
                    {
                        entry.Phase = FeedPhase.Finished;
                    }

                    break;
                }
            }

            if (entry.PopTime < PopDuration)
            {
                entry.PopTime += deltaTime;
                var t = Mathf.Clamp01(entry.PopTime / PopDuration);
                var scale = t < 0.32f
                    ? Mathf.Lerp(1f, 1.13f, t / 0.32f)
                    : Mathf.Lerp(
                        1.13f,
                        1f,
                        (t - 0.32f) / 0.68f);
                SetScale(entry.Root, scale);
            }
            else
            {
                SetScale(entry.Root, 1f);
            }
        }

        private static void SetPosition(
            VisualElement element,
            float x,
            float y)
        {
            element.style.translate = new Translate(
                new Length(x, LengthUnit.Pixel),
                new Length(y, LengthUnit.Pixel),
                0f);
        }

        private static void SetScale(
            VisualElement element,
            float scale)
        {
            element.style.scale =
                new Scale(new Vector2(scale, scale));
        }

        private static string FormatLabel(
            string displayName,
            long quantity) =>
            $"{(displayName ?? string.Empty).ToUpperInvariant()} ({quantity})";

        private static float EaseOutCubic(float value)
        {
            var inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private enum FeedPhase : byte
        {
            Entering = 1,
            Holding = 2,
            Exiting = 3,
            Finished = 4
        }

        private sealed class FeedEntry
        {
            public FeedEntry(
                VisualElement root,
                Label label,
                string displayName,
                long quantity)
            {
                Root = root;
                Label = label;
                DisplayName = displayName;
                Quantity = quantity;
                Phase = FeedPhase.Entering;
                PhaseTime = 0f;
                PopTime = PopDuration;
            }

            public VisualElement Root { get; }

            public Label Label { get; }

            public string DisplayName { get; }

            public long Quantity { get; set; }

            public FeedPhase Phase { get; set; }

            public float PhaseTime { get; set; }

            public float PopTime { get; set; }
        }
    }
}
