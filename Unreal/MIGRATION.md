# Changing My Life — Unreal migration

This directory is the independent Unreal Engine 5.8 project. The Unity project in
`../Game` remains the source-of-truth until each migration gate passes; migration
tools never save or rewrite Unity scenes.

## Completion contract

“100% migrated” means all of the following are true in a packaged Unreal build:

1. deterministic 20 Hz simulation and canonical hashes match the Unity fixtures;
2. inventory, crafting, machines, belts, gathering, mining, wood and airship work;
3. input, first-person equipment, HUDs, intro and persistence are feature-complete;
4. every shippable mesh, texture, animation, audio file and prefab has an Unreal owner;
5. Bootstrap, Intro and Starter Island have Unreal maps with validated gameplay;
6. terrain, vegetation, cliffs, water, clouds and day/night pass the reference captures;
7. no runtime code or asset depends on the Unity project.

The machine-readable inventory is generated into `Migration/unity_asset_manifest.json`.
Every source entry retains its Unity GUID and SHA-256 so missing or stale conversions
can be detected instead of silently accepted.

## Deterministic source import

`Tools/Run-UnityAssetImport.ps1` imports source meshes and textures into
`/Game/Migrated` while preserving Unity's directory hierarchy. The generated
`Migration/unity_asset_import_report.json` maps every Unity GUID and SHA-256 to
the imported Unreal object path. Re-running the importer is idempotent and never
writes to the Unity project.

## Running a migration step

`Tools/Invoke-CmlPython.ps1 -Script <name>.py [-RealRHI]` runs any step under
`Content/Python` inside UnrealEditor-Cmd and writes its report under
`Migration/`. Steps that author materials **must** be run at least once with
`-RealRHI`: under `-NullRHI` no shader is actually compiled, so a material that
cannot compile still reports success. The wrapper fails the run when the log
contains a material compile error, because the Python API never surfaces one.

## Embedded Unity textures

Nine `Texture2D` objects were serialised directly inside Unity `.asset` YAML and
so were invisible to the source importer. `Tools/extract_unity_embedded_textures.py`
decodes their RGBA32 payload to lossless PNG and `cml_embedded_texture_import.py`
imports them, registering each under its original Unity GUID in the asset import
report so material conversion resolves them like any other texture.

## Simulation foundation

`CML.Foundation` is ported into `CMLCore` and covered by automation tests
(`Automation RunTests CML.Core.Foundation`, 6 tests): stable ids and their
allocator, the 20 Hz tick, non-negative quantities, accumulator keys and the
exact fixed-denominator remainder accumulator.

Two deliberate departures from the C# original:

- Unity's `Unsigned128` delegated its exact intermediates to
  `System.Numerics.BigInteger`. Doing that here would put heap allocation and
  arbitrary precision on the 20 Hz path, so `FCMLUnsigned128` implements
  fixed-width 128-bit add / multiply / divmod directly.
- The C# API signalled refusal with exceptions (`OverflowException`,
  `InvalidOperationException`). The C++ port returns `bool` from every `TryX`
  instead, which preserves the contract that matters — an operation that cannot
  be represented fails the transaction rather than clamping or wrapping — while
  staying inside Unreal's no-exceptions convention.

## Canonical encoding and the logical state hash

The byte-exact half of gate 1 is ported and tested
(`Automation RunTests CML.Core.Simulation`):

- `FCMLCanonicalWriter` — shortest-form LEB128, ZigZag signed values,
  length-prefixed UTF-8. Verified against the standard encoding vectors, since
  one differing byte changes every canonical hash.
- `FCMLSha256` — SHA-256 implemented in-module and checked against three FIPS
  180-4 vectors. Unreal's `FPlatformMisc::GetSHA256Signature` has **no Windows
  implementation and asserts**, and the engine's other helpers are SHA-1, so
  delegating would have made the hash platform-dependent.
- `FCMLLogicalStateHasher` — the `LC-HLOGIC-v1` domain prefix, the zero
  separator and lowercase hex output, isolated from the state serialiser.

One known limitation, deliberately surfaced rather than hidden: Unity normalised
strings to NFC before encoding them and Unreal's Core has no NFC normaliser.
ASCII content keys are unaffected; anything outside ASCII is counted by
`FCMLCanonicalWriter::GetNumNonAsciiStrings` so a silent hash divergence is
impossible to miss.

`FCMLCanonicalStateSerializer` carries the root schema constant (sixteen tagged
fields) and the element serialisers whose inputs are already ported: stable ids,
the quantity map and the accumulator map. Each element is written into its own
writer and emitted as a length-prefixed blob, so a field's encoding never
depends on its neighbours.

Canonical *order* is part of the hash. Unity held these maps in a
`SortedDictionary`, so `SortQuantities` / `SortAccumulators` must run before
serialising an Unreal container, which has no ordering guarantee — two identical
worlds would otherwise hash differently. A test asserts exactly that.

`FCMLSimulationRecords` carries the tick records the root embeds: commands,
creation keys and records, and command rejections. Both enums
(`ECMLSimulationPhase`, `ECMLCommandRejectionReason`) are explicitly numbered
because their byte values are hashed — renumbering one would invalidate every
recorded fixture containing that phase or reason.

Root fields 1–13 and field 16 (the INV subtree) now have byte-exact encoders.
`FCMLInventorySimulationState` carries only what the canonical projection reads
— ids, container definition and slots — because capacity, catalog revision and
durability were deliberately left out of the Unity hash. Empty slots are encoded
as the none id with quantity zero: which position holds what decides how the
next insertion lands, so it is part of the future, and a test asserts that
moving a stack between slots changes the encoding.

Duplicate inventory ids are refused rather than hashed, since the encoding would
be ambiguous and no replay could reproduce it.

Field 14 (the AIR subtree) is ported with its state types. Everything in it is
quantised integer state — positions in millimetres, yaw in turn units, pilot
input in permille — with no float anywhere, which is what lets two machines
integrate the same flight and land on the same tick. The per-axis integration
remainders are hashed rather than treated as scratch, and a test asserts that
carrying one changes the encoding.

One departure forced by the engine: UnrealHeaderTool requires a zero entry on a
`UENUM`, and Unity's `AirshipRepairStatus` starts at `Damaged = 1`. `None = 0`
is added *below* the ported values instead of renumbering them, so every real
status keeps the byte value the fixtures were hashed with.

Field 15 (the MCH subtree) is ported with its state types. One thing Unity
expressed with object identity needed restating: a buffer's input and output are
literally the same port object, and the encoder emitted it once. C++ value
semantics cannot express that with a reference comparison, so
`FCMLMachineNodeState::bInputOutputAliased` states it explicitly — encoding the
port twice would put a crate's contents into the hash twice. A test asserts the
aliased form encodes shorter than two identical ports.

### The canonical root

`FCMLSimulationState` carries the sixteen-field canonical projection and
`TrySerializeRoot` assembles it, so **a whole world state now hashes
end-to-end**. `SortForCanonicalEncoding` must run first.

A malformed subtree fails the whole root rather than producing a
plausible-looking digest: a state with a duplicate id cannot be reproduced by a
replay, so hashing it would hide the defect instead of surfacing it.

### Cross-engine byte equality

`CML.Core.Simulation.UnityGoldenBytes` compares this encoder against bytes the
Unity build actually recorded, and they match exactly — which exercises the
writer, ZigZag, stable-id nesting and length prefixes end to end, not just
against my own expectations.

It only covers three of the four AIR collections, for a reason worth knowing:
**the Unity golden fixture is stale.** `NonEmptyAirshipStateMatchesGoldenBytesAndHash`
records AIR schema revision 4 with an eighteen-field airship element, while
`AirshipCanonicalSerializer` declares revision 5 and writes twenty-two fields —
the four extra being the hull repair state. That Unity test cannot be passing as
written. Its player, obstacle and landing-surface sections did not change
between those revisions, so those are used; the airship section is unusable
until the fixture is regenerated.

Gate 1 proper — a full replay's hashes matching — still needs the simulation
engine, since there is nothing to advance yet.

### Command queue

`FCMLSimulationCommandQueue` is ported and tested. Two rules are what make the
order reproducible rather than merely stable, and both refuse rather than
recover: a **sequence gap** is rejected, so ordering can never quietly fall back
to arrival order, and a **duplicate sequence** is rejected for the same reason.
Tick buckets are kept sorted on insert, so the canonical list never depends on
the order ticks were filled in.

### The engine

`FCMLSimulationEngine` is ported and tested. A tick advances a **deep copy** and
publishes it only after the twelfth phase commits, so a tick is a transaction
rather than a sequence of partial mutations: a test asserts that a failing phase
leaves the published state's hash byte-identical and the clock unmoved.

System order never depends on registration order — systems sort by phase, then
explicit order, then stable id, then type name. Two engines given the same
systems in *opposite* registration order reach the same state hash after five
ticks, which is the replay-determinism property in miniature.

Unity aborted a tick by throwing; a ported system returns `false` with a failure
cause instead, and the engine names the failing system in the result.

### Inventory transactions

`FCMLInventoryOperations` is ported and tested. Every operation is
**all-or-nothing**: a partial fit is refused outright and the inventory is left
untouched, because gameplay must fail a transaction rather than store some of an
amount. Two placement rules matter and are asserted:

- compatible partial stacks are topped up **before** an empty slot is opened, so
  an inventory does not fragment while a stack still has room;
- taking drains from the last slot towards the first, so the earliest slots keep
  their stacks and positions stay as stable as the operation allows.

Storable quantity is the smaller of two independent limits — the inventory's own
capacity and what the slots can physically hold — and a zero amount is a
succeeding no-op, not a refusal.

### Crafting

`FCMLCraftingRule` is ported and tested. A craft is **one transaction**: inputs
are taken and outputs stored against a working copy, and the caller's inventory
is replaced only once every step succeeded. The property worth stating is what
that buys — a craft whose output has nowhere to go **consumes nothing**, so
ingredients are never spent on a result that cannot be stored. A test sets that
case up deliberately and asserts the ingredient survives.

A craft count large enough to overflow the scaled amount is refused rather than
wrapping into a small, plausible number.

### Persistence

`FCMLSaveEnvelope` is ported and tested. One thing to know before treating
"persistence" as a large remaining item: **the Unity persistence layer is a
stub.** `CML.Persistence` contains exactly two files — the envelope and a
version constant — with no reader, no writer and no payload, and nothing in the
Unity runtime writes a save.

The migration therefore reproduces the boundary faithfully rather than inventing
a save format the Unity build never had. A save from a newer schema is refused,
because its unknown fields would otherwise be dropped silently. The payload
belongs to whichever engine implements saving first; if that is Unreal, this is
where it starts rather than something to be back-ported.

### Content catalog

`FCMLGameCatalog` is ported and tested: indexed lookup plus the validation rules
that protect determinism. The simulation reads content through this and nothing
else, so a catalog that fails validation must never reach it — every refusal
here is a case where two builds could otherwise disagree about the same world:

- a **duplicate id** would let lookup order decide which definition wins;
- a **dangling recipe reference** would let two builds disagree about the same
  craft;
- a **blank revision** would make two different catalogs indistinguishable,
  since the revision is hashed as root field 4;
- an **unsupported schema** may mean something different by the same fields.

A refusal reports the offending id, so it points at the content rather than just
saying no.

All six definition types are ported — items, recipes, containers, machines,
energy sources and island templates — with the validation rules that keep each
one internally consistent:

- a machine that **cannot hold its recipe's inputs or outputs** would accept a
  job it can never run;
- the energy pair has to agree: a self-actuated machine must require no external
  power and a powered one must require some, or the power phase cannot decide
  whether the machine may run;
- a fuel slot with no fuel named is a machine that can never run;
- an **inverted deposit range** would ask island generation for an impossible
  count.

`ECMLEnergyKind` keeps the Unity numbering including its gap at 1 — closing it
would change content that already refers to these values.

**Keys and durability.** The first pass of this port carried only each
definition's id, which quietly dropped two whole families of rule the Unity
validator enforces. Both are back:

- Every definition carries `FCMLDefinitionIdentity` — a content key and a
  localisation key, each required to be lowercase ASCII letters, digits, `.`,
  `_` or `-`. Content refers to definitions by key, so a key that is not
  canonical, or that two definitions share, is as ambiguous as a duplicate id.
  The key namespace is shared across all six types, exactly as in Unity: an item
  and a machine may not both answer to `item.ore`.
- An item's `MaximumDurability` may not be negative, and a durable item must
  have a stack size of exactly one. Wear belongs to one unit; a durable item
  that stacked would share one durability value between several tools.

`Identity` sits last in each struct so that the fields fixtures set positionally
stay leading, and the two new failure codes are appended to `ECMLCatalogFailure`
rather than slotted in — renumbering would quietly change what a failing catalog
reports.

`CMLContentIds.h` transcribes the published ids. The two gaps in the item
sequence (`0x0B`, `0x0C`) belonged to a rotor and a part that no longer exist and
must stay open: these values are hashed into the canonical state, so closing the
gaps would renumber every id after them and produce a different world from the
same actions.

### Fixed-point trigonometry

`FCMLFixedTurnTrig` is ported and tested. This is why two machines can fly the
same airship and land it on the same tick: a float sine gives each platform its
own rounding, while a CORDIC rotation over integers gives every platform the
same bits. The four cardinal turns are returned **exactly** rather than
approximated, because a quarter turn that is a bit off shows up as an airship
that never quite faces along an axis.

Division rounds halves **away from zero in both directions**. Rounding towards
zero would let the same speed travel further one way than the other, and the
drift would accumulate across a flight.

### Flight integration

`FCMLAirshipIntegration` is ported and tested. Speeds are authored per second
but applied per tick, so the leftover of each division is carried in a
**Euclidean** remainder — always non-negative. Truncating instead would lose a
little travel every tick; a signed remainder would make climbing and descending
drift against each other. A test asserts that 1001 mm/s covers exactly 1001 mm
after twenty ticks, in both directions.

Two conventions worth knowing before touching this code, both established by
reading the Unity reducer rather than assumed:

- the reducer computes `-|forward| * sin(pitch)`, so a **positive pitch turn
  dives** and a negative one climbs;
- the *absolute* forward speed feeds the vertical term, so reversing does not
  invert which way the nose points.

Travel is rotated into world space by the **new** yaw, so a turn takes effect on
the same tick it is commanded.

### Machine cycles

`FCMLMachineCycle` is ported and tested. The cycle spans two phases on purpose,
and the split is what makes a blocked machine safe: phase 7 starts a cycle
(spending inputs and fuel **up front**) and advances it one tick; phase 8
deposits the output of a finished cycle.

A cycle that finishes with nowhere to put its output stays finished and
undeposited — not lost, not re-run. Its ingredients were paid for once at the
start and must not be paid again, so progress does not advance past the duration
while it waits. A test blocks the output mid-flight, checks the cycle survives
and progress holds, then clears the blockage and checks it deposits.

Fuel burns once per cycle, not once per tick, and a machine admits only what its
active recipe consumes — a press fed from a mixed crate would otherwise fill its
slots with plates it can never use and deadlock itself.

### Mining and gathering

`FCMLHarvestRules` is ported and tested. The two rules stay **separate**, as in
Unity: mining is written around a tool — it reads the equipped slot, refuses on
an empty hand, counts hits against a tool-specific requirement and spends a
durability point on success. Teaching it to accept an empty hand would delete
the very check that stops a player mining stone with their fists.

Both commit their whole yield at once. A partial store would let a nearly full
inventory swallow one fibre of two and still consume the tuft, which is the
classic way matter goes missing. A blocked mining impact keeps the source one
hit from completion and spends no durability, so the next real impact retries
the whole transaction.

Content ids are transcribed from `CML.Content.ContentIds` and asserted against
those values in the test. They are hashed into the canonical state, so inventing
convenient numbers would silently produce a different world from the same
actions.

### Belt transport

`FCMLBeltTransport` is ported and tested. Two rules together produce
backpressure without anyone modelling it explicitly: every item moves forward
**front first** and never closes on the one ahead closer than the spacing, and
an item the destination refuses simply stays at the exit. The queue then forms
from the exit backwards, which is what a real belt does when the machine at its
end stops taking.

Backpressure here is a **position, not a flag** — a test blocks the destination
and asserts the three queued items sit at 1000, 800 and 600 mm rather than
collapsing together or being marked "waiting".

A blocked item holds its ground rather than being dragged back, and a lane
carrying something the machine's recipe does not consume backs up instead of
deadlocking the machine's input slots.

### Landing

`FCMLAirshipLanding` is ported and tested. A landing is **not a point test**:
the rule asks whether a continuous pad — a corridor as wide and deep as the
airship needs — exists in front of the ramp, and samples it on a fixed grid.
Testing only the centre would accept a surface with a hole exactly where a
player would step off, and a test asserts that a pad narrower than the corridor
is refused even though its centre point passes.

The reach search runs **outwards**, so the nearest legal pad wins rather than
whichever happens to be tested first. Obstacles standing between the ramp and
the pad block the landing even when the pad beyond them is perfect, and surface
containment goes through the fixed turn trigonometry so a rotated pad is tested
in its own frame rather than as an axis-aligned box.

### Swept collision

`FCMLAirshipCollision` is ported and tested. A tick can move an airship by up to
a metre, which is enough to pass straight through a thin obstacle if only the
endpoint is tested. The candidate pose is therefore **swept**: the move is
subdivided into as many samples as its largest component has millimetres (or
turn units), so the sampling can never step over something thinner than its own
resolution. A test places a 50 mm wall exactly between two clear endpoints and
asserts the sweep catches it.

That resolution also bounds the cost, which is why a candidate larger than any
legal one-tick flight is refused outright instead of swept with an unbounded
number of samples.

Overlap uses the **separating-axis theorem**, not a corner comparison. Comparing
corners is not an intersection test: a wall crossing the middle of the hull
contains none of the hull's corners and has none of its own inside the hull, so
a corner test calls a direct hit "clear" — which is exactly what the first
version of this port did until the test caught it. Since the hull only yaws,
height is a plain interval overlap and only the horizontal plane needs the four
candidate axes.

Each dot product of two Q30 axes lands in Q60 and is brought back to Q30 *before*
being scaled by a half-extent; multiplying first overflows int64 on any real
hull size.

### Spatial logistics graph

`FCMLMachineSpatialTopology` resolves the physical logistics graph from the
persistent node poses. **No edge is authored or stored.** A belt or funnel works
only while the required neighbours occupy the exact adjacent cells with
compatible facings, so removing any physical module disconnects the line on the
next authoritative tick without leaving a stale logical connection behind. That
is the property the tests pin: take a belt out of a working line and the crate
stops draining immediately.

The five steps run in a fixed order, and the order *is* the behaviour: funnels
pull, belts advance, belts deliver, belts load, funnels push. Because pulling
comes before loading, a piece travels the whole hop from crate to belt within one
tick rather than resting in the funnel for one — and only once the belt is
carrying something does the next piece wait in the funnel behind it.

`MachineAdmits` is deliberately stricter than the transfer rule. A belt keeps
pushing, so a machine that is mid-cycle, still holding its last output, or
already at its input buffer cap has to refuse delivery or it would silently
overfill.

Two things a fixture will get wrong if it is not told:

- A funnel has **one** slot, not two. Unity builds it with the same port object
  as both input and output, so a piece pulled in through the input has to be
  visible on the output — otherwise it goes in and never comes out. The same is
  true of a belt module and of a crate. In C++ these are two fields plus
  `bInputOutputAliased`, and the copy is kept in step after every move.
- Direction is polarity, not geometry. Reversing the drive on a line turns the
  same funnel from an extractor into an inserter, without moving anything.

One deviation: where C# threw on a node with an out-of-range quarter-turn
facing, this refuses the connection instead. A badly placed node is left simply
unconnected rather than aborting the whole tick.

### Belt module shape

`FCMLBeltModuleShape` is the geometric contract the whole belt topology rests
on. Before it existed the model assumed every belt was straight and level:
adjacency demanded the same axis and the same height between two modules. That
holds while only straight pieces exist, but a curve is *for* changing axis and an
incline is *for* changing height, so both fell outside the model — placeable but
feeding nothing. Not a wiring oversight: the concept was missing.

Each definition declares only two things — how far the exit turns relative to
the entry, and how far it rises — and everything else still derives from the
pose. Three points worth keeping:

- The right curve turns by 1 and the left by 3, which is −1. Both exist because
  a path that could only bend one way is not a path. The hand was verified in
  game, not deduced from the export: the FBX export flips an axis relative to
  Blender, so reading the source coordinates gives the opposite turn.
- On a straight run, reverse is the opposite of the pose and entry and exit
  agree. On a curve they do not, and telling them apart is what lets a path turn:
  reverse motion enters through the lateral exit and leaves through the geometric
  entry.
- Endpoint height is not an axis test. A curve owns two half-lines, not two
  infinite axes, and accepting the far side of an arm made a straight piece latch
  onto the wrong half of the curve.

### Slot rearrangement

`TryMoveWithinInventory` is the drag in the panel, and it is a rearrangement
rather than a transaction: nothing enters or leaves the inventory, so no
capacity is consulted. Three cases — onto an empty slot it splits, onto the same
item it merges up to the stack limit, and onto a *different* item it swaps.

Only a whole stack may swap. A partial swap is refused because the remainder of
the source would have nowhere to stay once its slot is taken by the other item,
and inventing a destination for it is worse than refusing. Dropping a stack
where it already is is not an error either: it is an ordinary gesture in the
panel and simply does nothing.

`FCMLSlotMoveCommandPayload` carries the two slot indices as four fixed
big-endian bytes, so decoding depends on neither a length nor the platform's
byte order. Everything else rides the command's own fields: the inventory in
`InitiatorId` and the amount in `QuantizedValue`, where zero means the whole
source stack.

### Transfers

`FCMLTransferRule` is the one authoritative way items move between two holders,
ported from `TransferRule`. One function serves the player emptying a crate into
a furnace, a crate feeding a press, and everything else. Two implementations of
"move n of item x" would eventually disagree about a stack limit or a capacity,
and the disagreement would show up as material appearing or vanishing — which no
test written against either half would catch.

What each destination will accept is the substance of the rule:

- a crate takes anything;
- a machine's **input** takes only what its active recipe consumes, so a press
  fed from a mixed crate cannot fill its slots with plates it can never use;
- a machine's **output** takes nothing from outside — it is a result buffer, and
  a hand-fed plate there would be indistinguishable from one the machine made;
- a **fuel** port takes only that machine's own fuel.

Capacity is two limits, not one. A port's physical room is capped again by the
machine's `InputBufferCapacityPerItem` and `FuelBufferCapacityPerItem`: without
the second cap a belt would fill every input slot with one ingredient and a
two-ingredient recipe would deadlock with nowhere to put the second.

Three deviations from the C# forced by value semantics:

- C# compared endpoints by object identity to catch a move from a holder to
  itself. Values cannot do that, so the resolved storage is named instead — an
  inventory by index, a port by its node and which field it is.
- C# threw a `SimulationInvariantException` if the apply half failed after the
  preflight passed. Here both sides are edited on copies and the caller's state
  is replaced only on success, so a broken invariant leaves the world untouched
  rather than half-moved.
- A buffer's input and output were one object in C#. Here the aliased copy is
  written back explicitly after a transfer, or the crate would show its old
  contents through its other face.

**A defect this uncovered.** `ECMLMachinePortKind` had no `Fuel` value. A port's
kind is written into the canonical hash as a byte, so every fuel-burning machine
would have hashed differently from Unity's.

### Flight controls

`FCMLAirshipControls` ports `AirshipReducer.UpdateFlightControls`. Nothing snaps:
every axis ramps toward its target on the same 80-tick acceleration budget, so
the airship takes a known number of ticks to reach full speed rather than one
that depends on frame timing.

Three asymmetries in the Unity original are deliberate and were kept:

- **Throttle is a change, not a target.** The stick trims the speed each tick;
  releasing it *holds* the speed. Lift is the opposite — it is a target, so
  releasing it returns to level. Reading throttle as a target would make the
  airship feel weightless.
- **Steering needs way on.** The reducer returns early when the forward speed is
  zero, so a parked airship can neither yaw nor pitch. The carried
  `YawIntegrationRemainder` is cleared at the same time, otherwise a stopped
  airship would creep round from a leftover fraction. Yaw authority then scales
  with speed and saturates at 4 m/s.
- **A reversal decelerates through zero.** Asking for the opposite direction
  folds the target to zero first rather than flinging the axis across it.

Gate 1 can only be evaluated once a full replay runs, since the hasher needs a
whole canonical state to hash.

## Prefabs

`cml_prefab_import.py` converts every Unity prefab into one Blueprint actor
whose component tree mirrors the Unity Transform hierarchy. `cml_unity_yaml.py`
is a dependency-free reader for Unity's serialised YAML, shared with the scene
conversion. Meshes and materials resolve through Unity GUIDs, never display
names. Report: `Migration/unity_prefab_import_report.json`.

Decisions worth knowing:

- Conversion runs in dependency order so a nested prefab already has a Blueprint
  when its parent needs a ChildActorComponent. A `PrefabInstance` that points at
  an imported model rather than a `.prefab` becomes a StaticMeshComponent.
- A Unity LODGroup lists its levels as sibling GameObjects; Unreal carries the
  LOD chain inside the StaticMesh, so only LOD0 becomes a component. Emitting
  the rest would draw the same rock three times.
- Regeneration reuses the existing Blueprint and empties its component tree
  rather than deleting the asset: Unreal's delete leaves the package loaded, the
  following create is refused while unattended, and every ChildActorComponent
  pointing at it would break.
- MonoBehaviour components are recorded as script GUIDs in the report, not
  fabricated as Blueprint logic. They belong to the gameplay gate.

## Scenes

`cml_scene_import.py` turns each Unity scene into an Unreal level under
`/Game/Maps`. A Unity scene is overwhelmingly prefab placements, so conversion
mostly spawns the Blueprint each `PrefabInstance` points at. Unity expresses a
placement relative to its parent, so the converter composes the whole parent
chain into a world transform before spawning; lights and cameras become their
Unreal counterparts. Report: `Migration/unity_scene_import_report.json`.

Unity's light intensity is a plain multiplier while Unreal's is photometric, so
the source value is written to a `CML.UnityIntensity` tag on the actor for the
lighting pass rather than copied across as if the units matched.

## HUD appearance

The original HUD is not a uGUI prefab. It is **UI Toolkit**, and its look lives
in `Art/UI/Inventory/InventoryHUD.uss` — which states its own design rules in
its header, and they are worth quoting because the first port broke all of them:

> the panel stays almost invisible: only the slots carry the milky white glass ·
> edges are 1 px hairlines, never chunky borders · no filled badges — numbers sit
> on the glass with a text outline · exactly one accent, the game's warm gold,
> used only for selection

`CMLHudStyle` transcribes the palette — cream `#F2E3C0`, gold `#D7A52D` — and
converts it from sRGB to linear **once**. Feeding 8-bit sRGB values straight into
an `FLinearColor` would wash the whole HUD out, and doing the conversion at each
call site would eventually be forgotten at one of them.

The slots carry the design: 62 px, white glass at 20% opacity, rising to 24%
when occupied and 28% when selected. Their rim is brightest on top and faintest
at the bottom, which is what reads as a lit pane rather than a drawn box.
Selection has no coloured ring and no badge — it is only slightly milkier glass
and a brighter top edge.

Two details that are easy to lose:

- The durability bar runs green → **yellow** → red. A single lerp from green to
  red passes through a muddy brown instead.
- The panel is drawn at 44% of screen height rather than centred, so the hotbar
  below it stays in view.

The style sheet is authored in pixels against a 1080p height, so the HUD scales
by screen height rather than being drawn at fixed pixel sizes.

## HUD

The Unity HUD splits into presenters and controllers. The presenters are pure
projections from simulation state to what a panel shows; the controllers are
UGUI wiring. Only the presenters carry behaviour, so they port to C++ beside the
simulation and are covered by the same automation tests, and the controllers are
rebuilt as UMG against them.

`FCMLInventoryHudPresenter` is the first, ported from
`InventoryHudPresenter`. It never stores, removes or moves anything: the HUD is a
view of the simulation, and a presenter that could write would be a second,
unsynchronised source of truth for what the player is carrying. It refuses
rather than improvising when the inventory is the wrong size or holds an item the
catalog does not define — either means the HUD and the simulation disagree about
the world, which is worse shown than caught.

Two deliberate deviations:

- Item appearance is a lookup table rather than Unity's chain of twenty-one `if`
  comparisons. Same data, same order of precedence, but adding an item is one
  line and cannot half-happen.
- **Durability is not invented.** Unity read wear off the inventory stack; this
  port keeps it in `FCMLToolState`, outside the canonical inventory, so the plain
  projection has none to read and does not claim a tool is undamaged. A caller
  holding the tool state calls `ProjectToolSlot` for that slot. The
  "has durability" flag stays separate from the value, because a broken tool
  reads zero and must not look like an item that has no durability at all.

`FCMLCraftingHudPresenter` follows, ported from `CraftingHudPresenter`. Whether
a craft is possible is not decided here: the panel asks `FCMLCraftingRule` and
reports what it says. A presenter that judged for itself would eventually
disagree with the rule that actually runs, and the button would lie. C# used a
`checked` block for the batch multiplication and let an overflow throw; there is
no equivalent here, so the multiplication is refused before it wraps — a wrapped
requirement would show a negative cost the player could "afford".

### What is *not* a presenter

Of the 83 files in Unity's presentation layer, 65 derive from `MonoBehaviour` or
touch uGUI/TextMeshPro directly. The four "factory presenters" are among them:
despite the name they are view components, so they belong to the UMG rebuild
rather than to CMLCore. The 18 engine-agnostic files are the ones that port
here.

## VFX

Two effects, two opposite techniques, both deliberate. Keeping them apart is the
whole job — making either match the other would be the easy mistake.

`FCMLImpactBurstGeometry` (from `PickaxeImpactBurst`) is **solid meshes and no
quad anywhere**: an irregular smoke puff, angular stone chips, long tumbling wood
splinters. Chips and splinters are objects, and a camera-facing textured quad
reads as a different game. These feed Niagara *mesh* emitters, never sprite
emitters. The puff's irregularity is two out-of-phase waves knocking a sphere
out of true — without them it is a ball, and a ball reads as a bubble.

`FCMLDustBurst` (from `FactoryDustBurst`) is **soft billboards, on purpose**. It
is dust, not debris: the particles are slowed by drag rather than thrown
ballistically, and they spread and fade instead of falling. Its sprite is
generated rather than imported — a white radial smoothstep falloff, so
overlapping particles merge into one cloud instead of stacking as visible discs
— which means the effect carries no asset, no meta and no reference that can go
missing from a build.

Two details that would drift silently if not stated:

- The Unity mesh builders swap the second and third index of every triangle.
  Reproducing the swap keeps faces pointing the way the artist saw them; dropping
  it turns every mesh inside out, which reads as an invisible effect rather than
  an obvious bug.
- The axis change Unity→Unreal is a *cyclic* permutation, so it preserves
  handedness and the winding carries over untouched. Positions are converted once
  here, metres to Unreal units, so emitter sizes stay plain multipliers.
- `DustTint` uses Unity's own luminance weights (0.299/0.587/0.114), not
  Rec. 709. A different formula shifts every dust colour in the game slightly —
  impossible to spot in one screenshot, obvious across a level.

The geometry is checked, not eyeballed: every mesh must be a closed surface with
each edge used exactly once in each direction, which catches holes, duplicated
faces and reversed winding together.

## Terrain

`TerrainData` is the one asset type Unity keeps in its binary `SerializedFile`
container even under Force Text serialisation — 18 of the project's 225 `.asset`
files, all terrains. The YAML reader cannot touch them.

The first attempt at this went through a C# editor script and `-executeMethod`,
which hung on `[Licensing::Module] Licensing is not yet initialized`.
`Tools/unity_serialized_file.py` replaces that with a reader for the binary
container itself, so the terrain extracts offline with no editor and no licence.
Editor-authored assets embed a full type tree, so the reader has no hardcoded
layout for any Unity class: the file describes its own structure and the reader
walks it. Two details are easy to get wrong and are checked rather than assumed:

- Unity writes GUIDs with the nibbles of each byte swapped, so a plain `hex()`
  matches no `.meta` file. The decoding is verified against the project's own
  `.meta` files.
- The file name means nothing. The scene loads a GUID, and that GUID belongs to
  `TerrainData_c7381312-….asset`, not to the `TD_StarterIsland.asset` sitting
  beside it. Unity leaves a TerrainData behind every time a terrain is rebuilt;
  the extractor follows the reference and reports the 27 orphans it skipped.

`Tools/extract_unity_terrain.py` writes the heightmap and one weightmap per
layer. Two conversions happen there:

- **Height scale.** Unity stores heights as 15-bit fractions of the terrain's
  height range. The divisor is not taken on trust: the height quadtree's root
  node records the terrain's own normalised minimum and maximum, which reads
  back 32766.001 against the raw range and confirms it.
- **Resolution.** Unity uses `2^n + 1` samples; Unreal uses
  `components x componentQuads + 1` with component sizes that are multiples of
  `2^n - 1`. No Unreal size divides Unity's 1024 quads, so one side has to give.
  The exporter pads rather than resamples: every Unity sample keeps its exact
  world position and its exact height, and the 47-quad surplus at the far edge
  is filled by clamping the border outward. Resampling would instead have moved
  every sample slightly — a flat skirt outside the original footprint is visible
  and obviously artificial, whereas a subtly wrong surface everywhere is neither.

Heights are re-based, not rescaled. Unreal's raw height 32768 is its zero plane,
so a Unity sample `r` is written as `32768 + r` and the landscape's Z scale
carries the metric conversion: one Unreal unit per Unity unit, no rounding.
Unity's X axis is Unreal's Y and Unity's Z is Unreal's X, so the heightfield is
transposed and the per-quad scales swap with the axes they belong to.

`ALandscape::Import` is editor-only C++ with no scripting exposure — the one
step of the migration Python cannot drive alone. `UCMLLandscapeImportLibrary`
(in `ChangingMyLifeEditor`) is the smallest bridge across that gap; every
decision about *what* to import stays in the exporter and in
`cml_terrain_import.py`. Unity normalises its splat weights so they total 1,
which is exactly what Unreal calls an additive alphamap.

### Verifying the terrain

Import reporting success only says the engine call returned.
`cml_terrain_verify.py` reads back the heights the landscape actually stores and
compares them against the exported raw file sample by sample, and checks the
actor transform against the world positions Unity would have produced. It
compares 25 728 heights across 24 rows: **0 mismatches**, transform exact.

Reading a single vertex row back has a trap worth recording. The engine maps a
vertex range to components with `CalcComponentIndicesNoOverlap`, which sends a
lone row sitting on a component's far edge to the component *after* the last one
— which does not exist, so nothing is written and the row reads back as zeroes.
That looked exactly like a corrupt final row of terrain. `ReadLandscapeHeightRow`
therefore reads a two-row band and returns the row that was asked for.

## Custom shader ports

The Unity project ships 24 hand-written URP shaders (value noise, triplanar
projection, vertex wind, hand-rolled lighting). They are ported as real HLSL in
`Shaders/*.ush`, published under the `/CML/` virtual shader path by
`FCMLCoreModule::StartupModule`, and called from material `Custom` expressions;
texture sampling stays in sampler nodes so every Unity texture property remains
an overridable Unreal texture parameter. Unreal parameter names are the Unity
property names verbatim, which makes instance population a mechanical copy.

World-space maths runs in Unity space (Y-up, metres) and is converted once at
the boundary by `CMLMaterialCommon.ush`, so each ported body stays line-for-line
comparable with its Unity original.

All 21 custom shaders that any material references are ported
(`Migration/unity_shader_port_report.json`), covering **48 of the 50**
custom-shader materials. Environment: CloudTall Tree, Stylized Surface, V4 Tree
Leaves, Original Cliff Mass, Terrain Splat, Foliage, Ground Cover, Ground
Detail, Underbody Terrain Rock, Terrain Reference Match, Vertical Rock Auto
Grass, Stylized Water, Atmospheric Sky. Clean room: Geometric Cloud, Grass Wind,
Cliff. Cinematics: Star Streak, Warp Tunnel, Portal Veil, Rift, Deep Space.

Not ported, with reason: `StarterIslandCliffRock.shader`, `ImpactFragmentMesh`
and `ImpactSmokeMesh` are referenced by no `.mat` (the impact pair is loaded at
runtime from `Resources` and belongs to the VFX gate).

### The vertex-colour defect

For a while every ported master that reads vertex colour failed to compile —
fifteen of them: surfaces, foliage, rock, grass, terrain and the cinematic
materials. A material that fails to compile is silently replaced by the engine's
default, so the symptom was not an error anybody would see. It was a grey world.

The compiler said only:

    error: no matching function for call to 'CMLStylizedSurfaceAlbedo'

which names the function and says nothing about which argument is wrong. Reading
the HLSL the compiler dumps to `Saved/ShaderDebugInfo` settled it in one line:
the generated Custom node declared `float3 VertexColorRGBA` while the ported
function takes `float4 VertexColor`.

`MaterialExpressionVertexColor`'s unnamed output looks four-channel in the
editor but arrives at a Custom node as `float3`. The alpha has to be re-appended
explicitly — exactly what `vector4()` already did for `VectorParameter`, with a
comment explaining why. The vertex-colour helper had a comment asserting the
opposite, and it was half right: that expression genuinely has no output named
`RGB`, unlike VectorParameter, so the append has to bind the *unnamed* output.
A correct observation had produced a wrong conclusion, which is why it survived.

This mattered beyond compiling. Unity's foliage and surface shaders carry real
data in vertex alpha — wind weight, wetness, blend masks — so a version that
compiled in RGB would have dropped a channel silently, which is worse than not
compiling at all.

**Cached failures look exactly like live ones.** The shader compiler caches a
failed compile, so one master kept reporting a failure that had already been
fixed, and the debug dump on disk was a day old. `cml_check_master_compiles.py`
force-recompiles every master and reports what it holds *now*, so "is it broken?"
is not answered from yesterday's log.

### Running a material-authoring step

The first run after a master changes reports compile errors for the *stale*
`.uasset` the editor loads before the script rebuilds it. Re-run the step: the
second pass loads the rebuilt asset from disk and is therefore also the real
verification that what was saved compiles. A step is only green when a clean
re-run produces no material compile errors.

Known deviations, recorded rather than hidden:

- Unity hand-rolled its diffuse wrap in the fragment (`_AmbientStrength`,
  `_ShadowFloor`, `_LandmassIndirectScale`). Unreal evaluates lighting outside
  the material, so those belong to the scene light rig; the affected masters
  carry a `CML.LightingStylization` metadata tag naming them.
- `M_CIN_HyperspaceStar` and `M_CIN_TransitPortal` reference shader GUID
  `650dd9526735d5b46b79224bc6e94025`, which no `.shader` in the Unity project
  defines. The reference is already broken in Unity, so those two stay on the
  generic PBR fallback.
- Unlit ports (water, sky, cinematics) cannot read URP's `Light` struct. The sun
  direction and colour come from the sky-atmosphere nodes, the environment
  reflection from the sky-light node, and `SampleSH` becomes the explicit
  `_CMLAmbientColor` parameter. Shadow attenuation has no unlit equivalent and
  is treated as fully lit.
- `Vertical Rock Auto Grass` reads an authored per-vertex macro normal from
  TEXCOORD1. Imported FBX meshes carry no such channel, and Unity's own validity
  gate then falls back to the geometric normal, which the port reproduces
  exactly by passing zero.
- The CloudTall tree's `chopData` (TEXCOORD1, written at runtime by
  TreeChopVoxelCarver) sits behind the `_CMLUseChopData` static switch, off by
  default, so imported meshes take the exact authored-bark path.
- The Terrain splat keeps Unity's `_Control` alphamap as a texture parameter
  instead of Unreal `LandscapeLayerWeight` nodes: Unity normalises the weights
  itself, including the automatic grass-to-cliff transfer, and rebuilding that
  on Landscape's own weight semantics would change the blend rather than port it.
