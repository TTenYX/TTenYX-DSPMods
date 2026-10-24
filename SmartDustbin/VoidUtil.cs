namespace SmartDustbin
{
    /// <summary>
    /// 共用工具：把消耗量记入对应工厂的产量统计（consumeRegister，即统计面板里的「消耗」）。
    /// 写入失败只影响统计显示，不影响消耗本身。
    /// </summary>
    internal static class VoidUtil
    {
        internal static void RegisterConsume(PlanetFactory factory, int itemId, int count)
        {
            if (factory == null || itemId <= 0 || count <= 0) return;
            try
            {
                var production = GameMain.statistics?.production;
                if (production == null || production.factoryStatPool == null) return;
                if (factory.index < 0 || factory.index >= production.factoryStatPool.Length) return;
                var register = production.factoryStatPool[factory.index]?.consumeRegister;
                if (register != null && itemId > 0 && itemId < register.Length)
                {
                    register[itemId] += count;
                }
            }
            catch
            {
                // 统计路径上的任何异常都不应中断物品消耗
            }
        }
    }
}
