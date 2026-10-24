# SmartDustbin

Turn existing switches into dustbins. No dependencies, no save data written — remove the mod and everything returns to vanilla.

- **Storage boxes** (small/large): drag the *automation input limit* slider to 0 and the box consumes **all** incoming items — manual drops, sorters, miners, drones, and belt/splitter drops from above.
- **Liquid tank**: switch *input* to **off** and the tank consumes every liquid arriving by belt or poured in by hand (liquids only).
- **Belts**: tag a belt with the vanilla **SIGNAL-404** icon tag to void items passing it. Tag count `0` = consume everything; `N` = consume N items per period (default 1 minute, configurable). Rates count individual items but consume whole stacks (max 4 per stack), so 600/min on fully stacked belts averages ~150 stacks per minute.

All consumption is recorded in the production statistics panel ("consume"). Configurable in `BepInEx/config/TTenYX.DSP.SmartDustbin.cfg` — every toggle applies immediately, no restart needed.

Game 0.10.35 · BepInEx 5 · TTenYX
