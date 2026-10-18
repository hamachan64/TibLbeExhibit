# Treasure Haul treasure models

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
