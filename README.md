# TrollingFishing

Cast multiple fishing lines with your fishing rod's secondary attack. The fishing rod also contains a fishing bag for storing fish and bait. <br> As your Fishing skill increases, fish are more likely to bite, your fishing bag grows larger, and items inside the bag become lighter.

![](https://i.ibb.co/wFVyhx8c/1.png) <br>
10 lines of fishing <br>

![](https://i.ibb.co/yFQ0Q23D/2.png) <br>
Stamina drain and skill gain from additional lines are configurable. <br>

![](https://i.ibb.co/0yGtdCD2/3.png) <br>
Fishing rod has a fishing bag within it. Your bag gets bigger proportional to your Fishing skill. <br>

![](https://i.ibb.co/Y4gbrDnj/4.png) <br>
Max 8x10 bag size. Fishchum mods's fishchum can go inside the fishing bag. <br> 

![](https://i.ibb.co/N2g4wBvX/tftf.gif) <br>
3 lines of fishing

https://youtu.be/Na_JCEdKrJY?si=C8nldbrs2quiRHGr <br>
Check out fishing 10 lines

## Features

### Fishing skill tuning

- Fish bite chance scales with Fishing skill.
- The default bite chance setting keeps vanilla at low skill and improves toward the configured target at Fishing 100.
<br>
- Fish extra-drop chance scales with Fishing skill.
- Extra-drop tuning uses a multiplier on the fish's original extra-drop chance, so rare drops stay rare unless configured otherwise.
- Check wiki for additional loot table of fish. https://valheim.fandom.com/wiki/Fish
- If you want to configure additional loot table of fish, try `DropNSpawn` https://thunderstore.io/c/valheim/p/sighsorry/DropNSpawn/

### FishingRod bag

- Hover a FishingRod in the player inventory and press the Use key to open its fishing bag.
- The bag is saved on the rod item itself.
- The bag accepts fish, fishing bait, and supported FishChum items.
- Caught fish try to go into the equipped FishingRod bag first.
- Bait stored inside the bag can be selected from inside the bag and used by the FishingRod.

- Bag content weight can become lighter as Fishing skill increases.

Bag size can scale with Fishing skill: <br>
- FishingRod bag size unlocks one tier per 10 Fishing levels: 4x2, 4x4, 6x4, then 8x4 through 8x10
- Bag UI size follows current Fishing skill and config. If the bag shrinks, items beyond the visible slots are preserved on the rod and reappear when enough slots are available again. If bag scaling is disabled, FishingRod bags use a fixed 8x4 size.

The bag accepts:

- Vanilla and modded fish items with item type `Fish`.`FishingBait`
- Fishchums from the mod `FishChum`


### Multi-line fishing

- The FishingRod secondary attack casts multiple fishing floats.
- The first float keeps the vanilla fishing flow.
- Additional floats reserve one bait each and return it when the cast fails in the same situations vanilla would return bait.
- Additional floats can use separate stamina and skill-gain multipliers while reeling.
- The number of floats, spread angle, and cast resource cost are configurable.

### AzuCraftyBoxes compatibility

If AzuCraftyBoxes is installed, FishingRod bags in the player's inventory can be exposed as nearby containers for crafting and cooking pulls.

The proxy container is created only around crafting/cooking lookup contexts and is cleaned up afterward. FishingRods inside external chests are not recursively exposed.

## Configuration
- Also, check `TrollingFishing.yml` to configure which bait each fish will nibble on.
```
[1 - General]

## If on, the configuration is locked and can be changed by server admins only.
# Setting type: Toggle
# Default value: On
# Acceptable values: Off, On
Lock Configuration = On

## If on, logs verbose fishing diagnostics such as multi-line float setup, water hits, bait reservations, and bag bait selection. Leave off for normal play.
# Setting type: Toggle
# Default value: Off
# Acceptable values: Off, On
Fishing Debug Logging = Off

[2 - Fishing Skill]

## Target bite chance at Fishing skill 100. Fish hook chance scales from vanilla baseHookChance at Fishing 0 toward max(baseHookChance, this value) at Fishing 100. Vanilla base chance is usually 0.10, so 0.30 makes eligible fish bite at up to 30% at Fishing 100 and 1.00 makes eligible fish always bite at Fishing 100.
# Setting type: Single
# Default value: 0.3
# Acceptable value range: From 0.1 to 1
Fishing Bite Chance Bonus Factor = 0.3

## Extra-drop chance multiplier at Fishing skill 100. Final chance = vanillaChance * (1 + factor * FishingSkillFactor). 0 keeps vanilla, 1 changes a 10% drop to 20% at Fishing 100, and 4 changes it to 50%.
# Setting type: Single
# Default value: 1
# Acceptable value range: From 0 to 4
Fishing Extra Drop Chance Bonus Factor = 1

[3 - Fishing Rod Bag]

## If on, pressing the Use key while hovering FishingRod in the player inventory opens an item-bound bag that accepts fish pickup items, fishing bait, and supported fish chum items.
# Setting type: Toggle
# Default value: On
# Acceptable values: Off, On
Fishing Rod Bag = On

## If on, FishingRod bag size unlocks one tier per 10 Fishing levels: 4x2, 4x4, 6x4, then 8x4 through 8x10. If off, FishingRod bags use a fixed 8x4 size. If the bag shrinks, overflow items remain stored on the rod and reappear when enough slots are available again.
# Setting type: Toggle
# Default value: On
# Acceptable values: Off, On
Fishing Rod Bag Scales With Fishing Skill = On

## If on, items stored inside FishingRod bags count toward the player's inventory weight while the rod is in the player's inventory.
# Setting type: Toggle
# Default value: On
# Acceptable values: Off, On
Fishing Rod Bag Counts Weight = On

## FishingRod bag item weight percent at Fishing skill 100. Fishing skill linearly scales bag item weight from 100% at skill 0 to this value at skill 100. 0 makes bag contents weightless at Fishing 100; 100 keeps full weight.
# Setting type: Int32
# Default value: 50
# Acceptable value range: From 0 to 100
Fishing Rod Bag Weight At Fishing 100 Percent = 50

## If on and AzuCraftyBoxes is installed, FishingRod bags in the player's inventory are exposed as nearby containers for crafting pulls. FishingRods stored inside external chests are not recursively exposed.
# Setting type: Toggle
# Default value: On
# Acceptable values: Off, On
Fishing Rod Bag AzuCraftyBoxes Compatibility = On

## If on, AzuCraftyBoxes integration also refreshes its private container registry/cache immediately after FishingRod bag proxy changes. If off, only the public AzuCraftyBoxes API is used, which is safer across AzuCraftyBoxes updates but may refresh later.
# Setting type: Toggle
# Default value: On
# Acceptable values: Off, On
Fishing Rod Bag AzuCraftyBoxes Aggressive Refresh = On

[4 - Multi Line Fishing]

## If on, FishingRod secondary casts multiple fishing floats. If off, the fishing secondary attack is blocked before ammo/resource use.
# Setting type: Toggle
# Default value: On
# Acceptable values: Off, On
Fishing Rod Multi Line = On

## Number of fishing floats fired by FishingRod secondary while Fishing Rod Multi Line is on.
# Setting type: Int32
# Default value: 3
# Acceptable value range: From 2 to 10
Fishing Rod Multi Line Count = 3

## Multiplier applied to the initial cast/draw stamina, eitr, and health costs for the multi-line secondary cast. Reeling costs are controlled by Extra Pull Stamina Factor.
# Setting type: Single
# Default value: 2
# Acceptable value range: From 0 to 5
Fishing Rod Multi Line Cast Resource Factor = 2

## Total horizontal spread angle in degrees across all floats fired by multi-line fishing.
# Setting type: Single
# Default value: 30
# Acceptable value range: From 0 to 90
Fishing Rod Multi Line Spread Angle = 30

## Multiplier applied to stamina consumed while reeling each additional multi-line fishing float. The primary-equivalent float always uses vanilla stamina cost. With 3 floats and 0.5, total reel stamina is roughly 1 + 2 * 0.5 = 2x vanilla.
# Setting type: Single
# Default value: 0.5
# Acceptable value range: From 0 to 5
Fishing Rod Multi Line Extra Pull Stamina Factor = 0.5

## Multiplier applied to Fishing skill progress while reeling each additional multi-line fishing float. The primary-equivalent float always uses vanilla skill progress. With 3 floats and 0.5, total reel skill progress is roughly 1 + 2 * 0.5 = 2x vanilla.
# Setting type: Single
# Default value: 0.5
# Acceptable value range: From 0 to 5
Fishing Rod Multi Line Extra Skill Raise Factor = 0.5

```

## Notes

- This mod targets the vanilla `FishingRod` prefab.
- FishingRod bag data is stored in the rod item's custom data.
- If a FishingRod bag shrinks, overflow items remain stored on the rod instead of being deleted.
- AzuCraftyBoxes integration is optional; the fishing bag works without it.
