# Treasure Haul treasure models

**Cart:** use [`Cart/Revision2/`](Cart/Revision2/README.md), the researched salvage-bin revision (4,432 triangles), with a Unity prefab and live cargo-value display. See [`Cart/RESEARCH.md`](Cart/RESEARCH.md) for references and design decisions. Moving-cart gameplay and host synchronization remain pending.

**Stage:** use [`Stage/Revision7/`](Stage/Revision7/README.md): 23 finds on stone (14 S + 9 M), 18 in masonry niches closed toward the entrance. Measured: 18/18 hidden from the practice row yet findable standing up close, 23/23 beam-reachable, no floor placement or mesh intersection. `Stage/Revision6/` (failed: 7/18) and Revision4–5 remain for comparison. Unity integration and PICO checks remain pending.

**Current reviewed deliverables:** use [`Reviewed/`](Reviewed/) for the corrected ten models and 30 FBX variants. See [`Reviewed/REVIEW.md`](Reviewed/REVIEW.md) for findings, budgets, and remaining device validation; [`Reviewed/details.png`](Reviewed/details.png) shows the revised treasure silhouettes. Files below and in `S/` are retained as the original authoring inputs for comparison. Run `review_treasures.py` after regenerating those inputs, then `verify_reviewed.py`. Do not import the older exports by mistake.

Ten original, realistic-leaning treasure models are authored and saved here. Every model is at or below 5,000 triangles including its glowing grip. All Blender scenes and FBX exports stay outside Unity's `Assets/` tree.

| Class | Model | Triangles | Glowing grips |
| :--- | :--- | ---: | ---: |
| S | Mask, Cup, Statuette | 4,684 / 4,792 / 4,664 | 1 each |
| S | Scarab Amulet, Ceremonial Lamp | 2,442 / 3,144 | 1 each |
| M | Amphora, Inscribed Stele | 4,128 / 1,610 | 2 each |
| L | Guardian Statue, Relic Chest | 4,686 / 2,468 | 2 each |
| Secret | Sun Mask | 4,568 | 1 |

Each model has three surface variants: the base bronze patina, cinnabar and gilt, and jade inlay. The original S models are in `S/`; their additional patterned FBX files and all seven new models' `.blend` scenes and three FBX variants are in this directory. Pattern image sources are in `patterns/`.

`Ten_Treasure_Showcase.blend` and `Ten_Treasure_Showcase.png` provide the saved lineup and its rendered preview. `build_more_treasures.py` regenerates the seven new models and FBX variants in an open Blender session; `verify_ten_treasures.py` round-trip checks triangle counts, dimensions, grip emission, and packed images. `ten_type_stats.json` and `ten_type_verification.json` contain the measurements.

All geometry and pattern images were made locally with Blender and procedural code. No external assets, paid generators, or Unity imports were used.
