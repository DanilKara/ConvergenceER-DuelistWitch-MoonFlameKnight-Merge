# Duelist Witch + Moon and Flame Knight — combined build for The Convergence 3.0.4

This is an **unofficial combined build** of two class mods for *Elden Ring: The Convergence 3.0.4*:

- **Duelist Witch v1.0.1**
- **Moon and Flame Knight v1.0** (the *Twin Moon Knight* class)

Each mod's readme says the two can't be installed together, because both use the same class slot in character creation. Several of the files they both overwrite also clash (regulation, texts, behavior, animations, Kale's dialogue, menu icons, effects). This build merges both mods into one `mod` folder, so **both classes work at the same time**, including with **Seamless Co-op**.

> Made as a fan contribution for the original author to review. All class content (models, animations, effects, texts, scripts) is the author's work. Only the combining was done here. If you are the author and want any of this changed or taken down, just say so.

## What changed compared to installing the two mods separately

| Area | Result |
|---|---|
| Character creation | **Duelist Witch keeps class index 29.** **Twin Moon Knight moved to a new slot, class index 30**, so it's now the last entry in the class list. |
| `regulation.bin` | All param rows from both mods are merged (AtkParam_Pc, BehaviorParam_PC, Bullet, EquipParamWeapon/Protector/Goods, Magic, SpEffect, SpEffectVfx, SwordArts, ShopLineup, ItemLotParam_map). The only real conflict was the class slot. Knight now uses `CharaInitParam` **3030 / 3160 / 3161** and `CharMakeMenuListItemParam` **100230** (value 30). |
| Class name / description | Knight's name and description moved from text ID 297119 to **297102** (`GR_MenuText` + `GR_LineHelp`), which is empty in vanilla and in Convergence. Witch keeps 297119. All 15 languages are merged. |
| Starting chapel event (`m10_01_00_00`) | Both mods added the same `InitializeEvent(29, 10012510, 60908, 29, …)`. A copy for index 30 was added: `InitializeEvent(30, 10012510, 60908, 30, 0, 0, 0, 0)`. |
| Merchant Kale (`t800006000.esd`) | Both mods added menu option 10. Now option **10 = "Duelist Witch's wares"** (shop 109530–109554) and option **11 = "Moon and Flame Knight's wares"** (shop 109500–109524, new state 101). |
| Behavior (`c0000.behbnd` → `Behaviors/c0000.hkx`) | Knight's 43 new clip generators were added to Witch's behavior across the 47 selectors (CMSGs) that need them. **Knight's `animationInternalId` values were remapped 19413–19445 → 19430–19462**, because both mods had started their new animations at 19413. |
| Animations (`c0000*.anibnd`) | All new TAE/HKX files from both mods. For `a778.tae` the Knight version is used: Witch only changed the file ID (20778 → 2778), and Knight has the same ID change plus one extra event in a778_040060. |
| HKS (`c0000.hks`) | 3-way text merge of both edits. Knight's script modules (`TwinMoonKnight.hks`, `Diversions.hks`, `Update.hks`, `Spells.hks`, `CommonIncludes.hks`) are included. |
| Menu | `05_dummy.tpf` holds the icons of both mods (137 textures). `01_common.sblytbnd` holds all layouts. Knight's new layout entries were renumbered (IDs 98–106) where they collided with Witch's IDs 60–68. Knight's `SB_Status_01.layout` edit is kept. |
| Effects (`sfxbnd_commoneffects_dlc02`) | All FXRs and resource lists from both mods. Entry IDs were renumbered per group. `f000645053` is absent, because both mods removed it. |
| Sound (`cs_smain.bnk`) | Taken from Duelist Witch (Knight doesn't change it). |
| `event/common.emevd` | Taken from Moon and Flame Knight (Witch doesn't change it). |

## Requirements

- Elden Ring with Shadow of the Erdtree
- The Convergence **3.0.4**, installed and working

## Installation

1. Back up your Convergence `mod` folder.
2. Copy the `mod` folder from this repository into your Convergence folder (the one that contains `mod`) and overwrite the files.
   You can install it directly over an existing Duelist Witch or Moon and Flame Knight install, because it contains every file from both mods.
3. Launch through the Convergence launcher as usual. If Seamless Co-op is installed (`SeamlessCoop/ersc.dll`), the launcher uses it automatically.

**Seamless Co-op:** everyone in the session must use this same combined build. If one player has only one of the mods, their `regulation.bin` will differ and the session can desync.

## Uninstall

Restore your `mod` folder backup.

## How it was merged (`tools/ertool`)

The merge was done with a small C# tool written for this purpose (.NET Framework 4.x, C# 5). It compiles with the `csc.exe` that ships with Windows. The tool:

- reads and writes DCX (DFLT / KRAK via `oo2core_6_win64.dll` / ZSTD via `libzstd.dll`), BND4, the encrypted `regulation.bin`, PARAM, FMG, EMEVD, ESD (`fsSL`), TPF and Havok 2018 tagfiles.
- does a 3-way merge of each file: Convergence 3.0.4 is the base, and the two mods are the two sides.
- relocates the class slot, Kale's menu option and the behavior animation IDs as described above.

Every merged file was checked:

- **regulation:** all 206,892 param rows were verified against the expected source mod.
- **archives:** every entry matches one of the two mods (or is the merged version), with no entries lost and no duplicate IDs.
- **behavior:** semantically compared, and each touched selector contains the union of both mods' clips.
- **file formats:** BND4, EMEVD and Havok files round-trip byte-for-byte unchanged.

Two encoding details turned out to be required, or the game crashes during boot (about 10 s after launch):

- **KRAK (Oodle) DCX** must be compressed with `seekChunkReset = true` and `seekChunkLen = 0x40000`.
- **`regulation.bin` (ZSTD DCX)** must use a 64 KB window (`windowLog 16`), with no content-size field and no checksum. It is then zero-padded to 16 bytes and encrypted with AES-256-CBC with PKCS7 padding, same as the shipped file.

The final build was tested with Convergence + Seamless Co-op: it boots and keeps running with no crash, where the earlier build crashed after about 10 s. In-game checks (character creation, Kale's shops, both classes' skills) are still to be done.

Build (from `tools/ertool`, with `oo2core_6_win64.dll` from the game folder and `libzstd.dll` next to the exe):

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -platform:x64 -optimize -out:ertool.exe *.cs
```
