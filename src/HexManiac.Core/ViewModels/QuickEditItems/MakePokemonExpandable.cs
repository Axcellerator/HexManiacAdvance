﻿using HavenSoft.HexManiac.Core.Models;
using HavenSoft.HexManiac.Core.Models.Runs;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace HavenSoft.HexManiac.Core.ViewModels.QuickEditItems {
   public class MakePokemonExpandable : IQuickEditItem {
      public string Name => "Make Pokemon Expandable";

      public string Description => "Make it possible to expand the number of pokemon in the game." + Environment.NewLine +
         "Change the game's code to make the Egg/Unown pokemon IDs update as new pokemon are added." + Environment.NewLine +
         "TODO Update Hall-of-Fame data to allow 16 bits per pokemon." + Environment.NewLine +
         "Add additional bits for pokedex seen/caught flags for the new pokemon." + Environment.NewLine;

      public string WikiLink => "https://github.com/haven1433/HexManiacAdvance/wiki/Pokemon-Expansion-Explained";

      public event EventHandler CanRunChanged;

      public bool CanRun(IViewPort viewPort) => true;

      public async Task<ErrorInfo> Run(IViewPort viewPortInterface) {
         var viewPort = (IEditableViewPort)viewPortInterface;
         var token = viewPort.ChangeHistory.CurrentChange;

         // TODO when expanding pokemon, make sure that the new data.pokemon.count constant actually gets updated...
         // TODO when expanding pokedex, make sure that the new data.pokedex.count constant actually gets updated...
         // TODO update pokedex search alpha?
         // TODO update pokedex search type?
         // TODO pokemon sprite/palette index doesn't update automatically
         // TODO update length of cry tables automatically when expanding pokemon?

         // update constants and allow for automatic code updates when the number of pokemon changes
         var pokecount = UpdateConstants(viewPort, token);
         var loadPokeCountFunctions = AddPokemonThumbConstantCode(viewPort, token, pokecount);
         UpdatePokemonThumbConstants(viewPort, token, loadPokeCountFunctions);

         // update constant and allow for automatic code updates when the size of the pokedex changes
         var dexCount = viewPort.Model.GetTable(HardcodeTablesModel.DexInfoTableName).ElementCount; // TODO this can fail...
         var loadDexCountFunctions = AddPokedexThumbConstantCode(viewPort, token, dexCount);
         UpdatePokedexThumbConstants(viewPort, token, dexCount, loadDexCountFunctions);

         // fix the really stupid table-index switch of PlayCryInternal
         UpdatePlayCryInternal(viewPort, token);

         // still have 0xA4 free bytes at 157878
         // still have 0x98 free at 072108

         // cleanup: can we use the reclaimed space from the PlayCryInternal function to fit all the new code we need?
         // instead of having to move the 0xF0 length switch table from 0x15782C?
         return ErrorInfo.NoError;
      }

      private int UpdateConstants(IEditableViewPort viewPort, ModelDelta token) {
         var model = viewPort.Model;
         var pokecount = model.GetTable(HardcodeTablesModel.PokemonNameTable).ElementCount; // TODO this can fail...

         var pokeCount0 = new[] { 0x168E09, 0x16B2BC }; // 2 bytes, equal to number of pokemon
         var pokeCount1 = new[] { 0x043240, 0x0CB160, 0x0CB16C, 0x14420C }; // 2 bytes, equal to number of pokemon-1
         var pokeCount2 = new[] { 0x12EAB0, }; // 2 bytes, equal to number of pokemon+1

         foreach (var address in pokeCount0) model.WriteMultiByteValue(address, 2, token, pokecount);
         foreach (var address in pokeCount1) model.WriteMultiByteValue(address, 2, token, pokecount - 1);
         foreach (var address in pokeCount2) model.WriteMultiByteValue(address, 2, token, pokecount + 1);

         foreach (var address in pokeCount0) model.ObserveRunWritten(token, new WordRun(address, "data.pokemon.count", 2, 0, 1));
         foreach (var address in pokeCount1) model.ObserveRunWritten(token, new WordRun(address, "data.pokemon.count", 2, -1, 1));
         foreach (var address in pokeCount2) model.ObserveRunWritten(token, new WordRun(address, "data.pokemon.count", 2, 1, 1));

         return pokecount;
      }

      private int AddPokemonThumbConstantCode(IEditableViewPort viewPort, ModelDelta token, int pokecount) {
         var model = viewPort.Model;

         // 15782C, for 0xF0 bytes, is a switch-statement table. We can move the switch table to reclaim this space for new values
         var originalSwitchTableStart = 0x15782C;
         var switchTable = new byte[0xF0];
         model.ClearFormat(token, originalSwitchTableStart - 4, switchTable.Length + 4);
         Array.Copy(model.RawData, originalSwitchTableStart, switchTable, 0, switchTable.Length);
         var newSwitchTableStart = model.FindFreeSpace(model.FreeSpaceStart, 0xF0);
         token.ChangeData(model, newSwitchTableStart, switchTable);
         for (int i = 0; i < switchTable.Length; i++) switchTable[i] = 0xFF;
         token.ChangeData(model, originalSwitchTableStart, switchTable);
         model.WritePointer(token, originalSwitchTableStart - 4, newSwitchTableStart);

         var newCode = viewPort.Tools.CodeTool.Parser.Compile(token, model, originalSwitchTableStart,
            "ldr r0, [pc, <pokecount>]", // 0
            "bx  lr",
            "ldr r1, [pc, <pokecount>]", // 4
            "bx  lr",
            "ldr r2, [pc, <pokecount>]", // 8
            "bx  lr",
            "ldr r3, [pc, <pokecount>]", // 12
            "bx  lr",
            "ldr r4, [pc, <pokecount>]", // 16
            "bx  lr",
            "ldr r5, [pc, <pokecount>]", // 20
            "bx  lr",
            "ldr r6, [pc, <pokecount>]", // 24
            "bx  lr",
            "ldr r7, [pc, <pokecount>]", // 28
            "bx  lr",
            "ldr r0, [pc, <pokecountMinusTwo>]", // 32
            "bx  lr",
            "ldr r1, [pc, <pokecountMinusTwoShiftToHighBits>]", // 36
            "bx  lr",
            "ldr r4, [pc, <pokecount>]",                        // 40: r4=pokecount + r0 - 161 : see Menu2_GetMonSpriteAnchorCoord, species = SPECIES_OLD_UNOWN_B + unownLetter - 1
            "add r4, r4, r0",
            "sub r4, 161",
            "bx  lr",
            "pokecount: .word 0",                               // 48
            "pokecountMinusTwo: .word 0",                       // 52
            "pokecountMinusTwoShiftToHighBits: .word 0"         // 56
            ).ToArray();
         token.ChangeData(model, originalSwitchTableStart, newCode);
         int wordOffset = 48;

         model.WriteMultiByteValue(originalSwitchTableStart + wordOffset, 4, token, pokecount);
         model.WriteMultiByteValue(originalSwitchTableStart + wordOffset + 4, 4, token, pokecount - 2);
         model.WriteMultiByteValue(originalSwitchTableStart + wordOffset + 8, 4, token, (pokecount - 2) << 16);

         model.ObserveRunWritten(token, new WordRun(originalSwitchTableStart + wordOffset, "data.pokemon.count", 2, 0, 1));
         model.ObserveRunWritten(token, new WordRun(originalSwitchTableStart + wordOffset + 4, "data.pokemon.count", 2, -2, 1));
         model.ObserveRunWritten(token, new WordRun(originalSwitchTableStart + wordOffset + 10, "data.pokemon.count", 2, -2, 1));

         return originalSwitchTableStart;
      }

      private void UpdatePokemonThumbConstants(IEditableViewPort viewPort, ModelDelta token, int pokecountFunctionAddress) {
         var model = viewPort.Model;
         byte[] compile(int adr, int reg) => viewPort.Tools.CodeTool.Parser.Compile(token, model, adr, $"bl <{pokecountFunctionAddress + reg * 4:X6}>").ToArray();
         var registerUpdates = new[] {
            new[] { 0x00FFFA, 0x0118D0, 0x02A81A, 0x02CEAC, 0x07FE12, // r0
                    0x0E6590, 0x0A0470, 0x0F31B2, 0x0FBFD6, 0x0DABB4,
                    0x0DAC1A, 0x094A1A, 0x043766, 0x043C42, 0x043E5C,
                    0x0440FE, 0x113EC0, 0x113EE0, 0x05148E, 0x052174,
                    0x05287E, 0x0535D0, 0x04FAA2, 0x04FC5E, 0x04FC8C,
                    0x04FCA2, 0x04FD04, 0x11AC1E, 0x11ADD4, 0x11ADF0,
                    0x11B030, 0x11B19A, 0x074658, 0x074728, 0x074788,
                    0x076BEC, 0x076CC8, 0x011F74, 0x0459CC, 0x00EC94,
                    0x00ED6C, 0x00F0E4, 0x00F1B0, 0x096E72, 0x096F86,
                    0x0970A6, 0x09713E, 0x0971D2, 0x0971FE, 0x040FDA,
                    0x09700A,
            },
            new[] { 0x0C839C, 0x0C8756, 0x0C882C, 0x0392CE, 0x0394FC, // r1
                    0x03994C, 0x039BC0, 0x03A234, 0x00D7E4, 0x00D854,
                    0x01196A, 0x013384, 0x013400, 0x01348C, 0x025BF8,
                    0x026DF0, 0x02B9A8, 0x02C9C8, 0x019CA4, 0x019D5A,
                    0x0CAD20, 0x0F23E4, 0x0F2E32, 0x0F32A4, 0x0F32E6,
                    0x040D0C, 0x15EBE0, 0x1193FA, 0x11B11C, 0x074624,
                    0x0746F0, 0x076BD8, 0x076C98, 0x011F3C, 0x096F78,
            },
            new[] { 0x026E8E, 0x00ED3E, 0x00F182, 0x096FFC, },        // r2
            new[] { 0x04FAE6 }, // r3
            new[] { 0x040036, 0x0401A6, 0x12EAA4 }, // r4
            new int[0], // r5
            new int[0], // r6
            new int[]{ 0x0A0224 }, // r7
         };
         for (int register = 0; register < 8; register++) {
            foreach (var address in registerUpdates[register]) {
               token.ChangeData(model, address, compile(address, register));
            }
         }

         token.ChangeData(model, 0x103726, viewPort.Tools.CodeTool.Parser.Compile(token, model, 0x103726, $"bl <{pokecountFunctionAddress + 32:X6}>").ToArray()); // pokedex_screen
         token.ChangeData(model, 0x0BECA2, viewPort.Tools.CodeTool.Parser.Compile(token, model, 0x0BECA2, $"bl <{pokecountFunctionAddress + 36:X6}>").ToArray()); // ReadMail
         token.ChangeData(model, 0x12EAB4, viewPort.Tools.CodeTool.Parser.Compile(token, model, 0x12EAB4, $"bl <{pokecountFunctionAddress + 40:X6}>").ToArray()); // Menu2_GetMonSpriteAnchorCoord, species = SPECIES_OLD_UNOWN_B + unownLetter - 1
      }

      private int AddPokedexThumbConstantCode(IEditableViewPort viewPort, ModelDelta token, int dexCount) {
         var model = viewPort.Model;

         var insertPoint = 0x157868;
         var newCode = viewPort.Tools.CodeTool.Parser.Compile(token, model, insertPoint,
            "ldr r0, [pc, <dexcount>]", // 0
            "lsl r0, r0, #3",
            "bx  lr",
            "nop",
            "ldr r2, [pc, <dexcount>]", // 8
            "bx  lr",
            "dexcount: .word 0"         // 12
            ).ToArray();
         token.ChangeData(model, insertPoint, newCode);
         int wordOffset = 12;

         model.WriteMultiByteValue(insertPoint + wordOffset, 4, token, dexCount);
         model.ObserveRunWritten(token, new WordRun(insertPoint + wordOffset, "data.pokedex.count", 2, 0, 1));

         return insertPoint;
      }

      private void UpdatePokedexThumbConstants(IEditableViewPort viewPort, ModelDelta token, int dexCount, int dexcountFunctionAddress) {
         var countMinusOne = new[] { 0x088EA4, 0x1037D4, 0x103870, 0x103920, 0x104C28 };
         foreach (var address in countMinusOne) {
            viewPort.Model.WriteMultiByteValue(address, 2, token, dexCount - 1);
            viewPort.Model.ObserveRunWritten(token, new WordRun(address, "data.pokedex.count", 2, -1, 1));
         }

         var model = viewPort.Model;
         byte[] compile(int adr, int offset) => viewPort.Tools.CodeTool.Parser.Compile(token, model, adr, $"bl <{dexcountFunctionAddress + offset:X6}>").ToArray();

         // update  1025EC to bl <r0=dex_size*8>
         // update  103534 to bl <r2=dex_size>
         compile(0x1025EC, 0);
         compile(0x103534, 8);
      }

      /// <summary>
      /// PlayCryInternal has a switch-statement at the end that limits the number of pokemon to 512 (128*4).
      /// We can rewrite that function to remove the limit and simplify the code.
      /// </summary>
      private void UpdatePlayCryInternal(IEditableViewPort viewPort, ModelDelta token) {
         var scriptStart = 0x0720C2;
         var scriptLength = 35 * 2;
         var gMPlay_PokemonCry = 0x02037ECC;
         var SpeciesToCryId = 0x043304;
         var SetPokemonCryTone = 0x1DE638;
         var script = @$"
            sound_PlayCryInternal_After_SetPokemonCryPriority:
               mov   r0, r7
               bl    <{SpeciesToCryId:X6}>
               mov   r1, #12
               mul   r0, r1
               mov   r1, r9   @ if (v0 == 0) use sound.pokemon.cry.normal. Else use sound.pokemon.cry.growl
               cmp   r1, #0
               beq   <use_normal_cry>
               ldr   r1, [pc, <growl_cry>]
               b     <next>
            use_normal_cry:
               ldr   r1, [pc, <normal_cry>]
               b     <next>
               nop
            growl_cry:  .word <sound.pokemon.cry.growl>
            normal_cry: .word <sound.pokemon.cry.normal>
            0720E4:     .word 0x3A98   @ 15000, needed to be right here for earlier in the function.
            next:
               add   r0, r0, r1 @ r0 = index into chosen table
               bl    <{SetPokemonCryTone:X6}>
               ldr   r1, [pc, <gMPlay_PokemonCry>]
               str   r0, [r1, #0]
               add   sp, #4
               pop   {{r3-r5}}
               mov   r8, r3
               mov   r9, r4
               mov   r10, r5
               pop   {{r4-r7}}
               pop   {{r0}}
               bx    r0
               nop
            gMPlay_PokemonCry: .word 0x{gMPlay_PokemonCry:X8}";
         viewPort.Tools.CodeTool.Parser.Compile(token, viewPort.Model, 0x0720C2, script.SplitLines());

         // clear 152 bytes after that are no longer needed
         viewPort.Model.ClearFormatAndData(token, scriptStart + scriptLength, 152);
      }

      public void TabChanged() => CanRunChanged?.Invoke(this, EventArgs.Empty);
   }
}
