using System;
using UnityEngine;

namespace OskarMike.Items
{
    /// <summary>테이블 등급. 높을수록 고티어(고가치) 아이템이 잘 나온다. 확률표는 <see cref="TableLootSpawner"/>.</summary>
    public enum LootTableGrade : byte
    {
        [InspectorName("일반")] Common,
        [InspectorName("고급")] Uncommon,
        [InspectorName("희귀")] Rare,
        [InspectorName("영웅")] Epic,
        [InspectorName("전설")] Legendary
    }

    /// <summary><see cref="LootCategory"/> 다중 선택용 마스크. 0(Nothing)이면 전체 허용.</summary>
    [Flags]
    public enum LootCategoryMask
    {
        [InspectorName("산업")] Industrial = 1 << (int)LootCategory.Industrial,
        [InspectorName("전자")] Electronics = 1 << (int)LootCategory.Electronics,
        [InspectorName("잡동사니")] Junk = 1 << (int)LootCategory.Junk,
        [InspectorName("귀중품")] Valuables = 1 << (int)LootCategory.Valuables,
        [InspectorName("군용")] Military = 1 << (int)LootCategory.Military,
        [InspectorName("정보")] Intel = 1 << (int)LootCategory.Intel,
        [InspectorName("소모품")] Consumables = 1 << (int)LootCategory.Consumables,
        [InspectorName("열쇠")] Key = 1 << (int)LootCategory.Key,
        [InspectorName("마약")] Drugs = 1 << (int)LootCategory.Drugs
    }

    public static class LootCategoryMaskExtensions
    {
        public static bool Allows(this LootCategoryMask mask, LootCategory category)
            => mask == 0 || ((int)mask & (1 << (int)category)) != 0;
    }
}
