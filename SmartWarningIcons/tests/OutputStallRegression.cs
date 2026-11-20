using System;

namespace SmartWarningIcons.Tests
{
    public sealed partial class SelfTestPlugin
    {
        private const int NativeCycle = 600000;
        private const int NativeBatch = 2;

        private static string TestNativeOutputStall(bool isLab)
        {
            var fixture = new Fixture();
            var system = fixture.Local.factorySystem;
            var entityId = isLab ? 2 : 1;
            var capacity = isLab ? 10 : 20; // 1× speed lab / single-output Assemble recipe of two per batch.
            var productId = isLab ? LabComponent.matrixIds[0] : 1102;
            var recipe = new RecipeExecuteData(new[] { 1101 }, new[] { 1 }, new[] { productId },
                new[] { NativeBatch }, NativeCycle, NativeCycle, false);
            var products = new int[productId + 1];
            var inputs = new int[1102];

            if (isLab)
            {
                var lab = ProductionLab(NativeCycle, true, 3, 6);
                lab.recipeExecuteData = recipe;
                lab.incServed = new int[1];
                lab.speed = lab.speedOverride = 10000;
                system.labPool[1] = lab;
            }
            else
            {
                var assembler = Assembler(NativeCycle, true, 3, 6);
                assembler.recipeExecuteData = recipe;
                assembler.recipeType = ERecipeType.Assemble;
                assembler.incServed = new int[1];
                assembler.speed = assembler.speedOverride = 10000;
                system.assemblerPool[1] = assembler;
            }

            void FinishAnotherCycle(int stock, int served)
            {
                if (isLab)
                {
                    ref var lab = ref system.labPool[1];
                    lab.time = NativeCycle;
                    lab.produced[0] = stock;
                    lab.served[0] = served;
                }
                else
                {
                    ref var assembler = ref system.assemblerPool[1];
                    assembler.time = NativeCycle;
                    assembler.produced[0] = stock;
                    assembler.served[0] = served;
                }
            }

            void Tick(string phase, bool shouldRun, int expectedStock, uint sign)
            {
                uint nativeResult;
                bool replicating;
                int time, produced;
                if (isLab)
                {
                    ref var lab = ref system.labPool[1];
                    nativeResult = lab.InternalUpdateAssemble(1f, products, inputs);
                    (replicating, time, produced) = (lab.replicating, lab.time, lab.produced[0]);
                }
                else
                {
                    ref var assembler = ref system.assemblerPool[1];
                    nativeResult = assembler.InternalUpdate(1f, products, inputs);
                    (replicating, time, produced) = (assembler.replicating, assembler.time, assembler.produced[0]);
                }
                Require((nativeResult != 0) == shouldRun && replicating == shouldRun && produced == expectedStock,
                    $"{phase}: native result={nativeResult}, running={replicating}, cycle={time}, stock={produced}");
                Require(shouldRun ? time > 0 && time < NativeCycle : time == (sign == Yellow ? NativeCycle : 0),
                    $"{phase}: unexpected recipe progress {time}");

                // Mirror the native FactorySystem mapping, then invoke the real patched frame-end method.
                fixture.Local.entitySignPool[entityId].signType = nativeResult == 0 ? SignData.NOT_WORKING : SignData.NONE;
                fixture.Tick();
                var actual = fixture.Local.entitySignPool[entityId].signType;
                Require(actual == sign, $"{phase}: stock={produced}, running={replicating}; expected sign {sign}, actual {actual}");
            }

            Tick("buffer has room for output, machine continues", true, 8, SignData.NONE);
            FinishAnotherCycle(capacity, 3);
            Tick("finished batch cannot fit", false, capacity, Yellow);
            Tick("still completely blocked", false, capacity, Yellow);
            // Free room for a batch but leave existing stock. The released batch fills it again;
            // nevertheless the next cycle can start, so yellow must already be gone.
            if (isLab) system.labPool[1].produced[0] -= NativeBatch;
            else system.assemblerPool[1].produced[0] -= NativeBatch;
            Tick("partial unloading resumes production despite high stock", true, capacity, SignData.NONE);
            FinishAnotherCycle(capacity - NativeBatch, 0);
            Tick("output was accepted but next batch lacks input", false, capacity, Red);
            return $"native signs 0/25/25/0/31; output still {capacity} when production resumes";
        }

        private static string TestNativeMultiOutputStall()
        {
            var fixture = new Fixture();
            ref var assembler = ref fixture.Local.factorySystem.assemblerPool[1];
            assembler = Assembler(NativeCycle, true, 2, 0);
            assembler.recipeType = ERecipeType.Refine;
            assembler.speed = assembler.speedOverride = 10000;
            assembler.recipeExecuteData = new RecipeExecuteData(new[] { 1101 }, new[] { 1 },
                new[] { 1102, 1103 }, new[] { 2, 1 }, NativeCycle, NativeCycle, false);
            assembler.incServed = new int[1];
            assembler.produced = new[] { 0, 20 };
            var products = new int[1104];
            var inputs = new int[1102];
            var result = assembler.InternalUpdate(1f, products, inputs);
            Require(result == 0 && assembler.produced[0] == 0 && !assembler.replicating, "native update must block both outputs when a single by-product cannot fit");
            fixture.Local.entitySignPool[1].signType = SignData.NOT_WORKING;
            fixture.Tick();
            fixture.Expect(1, Yellow);

            assembler.produced[1]--;
            result = assembler.InternalUpdate(1f, products, inputs);
            Require(result != 0 && assembler.replicating && assembler.produced[0] == 2 && assembler.produced[1] == 20,
                "native update must resume after freeing one by-product slot");
            fixture.Local.entitySignPool[1].signType = SignData.NONE;
            fixture.Tick();
            return fixture.Expect(1, 0) + "; blocked by-product sign 25 cleared after one item was removed";
        }
    }
}
