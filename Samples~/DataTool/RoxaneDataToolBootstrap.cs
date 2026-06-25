// =====================================================================
// SAMPLE / TEMPLATE — NOT COMPILED (Samples~ folder).
//
// Shows how Roxane would REPLACE the current DataToolServiceManager with the
// reusable DataToolKit module in Phase 2. The game-specific pieces
// (init/config table, force-update migration, ServiceLocator wiring) stay here
// in the app; only DataToolService does the generic load orchestration.
//
// Order matters:
//   1) LoadInitConfigAndMigration  -> app-specific (ClientMiscTable + force-update)
//   2) RegisterAll                 -> register every other table + its fallback + DI target
//   3) _service.InitializeAsync()  -> module: write-key compare -> parallel -> heavy
//
// References app-specific types (ServiceLocator, GameData, GameDataLocalFactory,
// MigrationController, ...) that won't exist in a fresh project, so this file is guarded
// by DATATOOLKIT_SAMPLES and stays inert by default. Read it as a reference; to compile,
// add DATATOOLKIT_SAMPLES to your Scripting Define Symbols and adapt the type names.
// =====================================================================
#if DATATOOLKIT_SAMPLES

using System;
using Cysharp.Threading.Tasks;
using DataToolKit;
using Game4Creators;
using GameData;
using GameVanilla.Game.Database;
using UnityEngine;

namespace Roxane.DataTool
{
    public class RoxaneDataToolBootstrap
    {
        private DataToolService _service;

        public async UniTask InitializeAsync(ProjectConfig projectConfig)
        {
            _service = new DataToolService(
                new RoxaneDataToolConfig(),
                new RoxaneDataToolNetwork(),
                new RoxaneWriteKeyStore());

            // 1) App-specific bootstrap (NOT part of the module).
            await LoadInitConfigAndMigration(projectConfig);

            // 2) Register the rest of the tables.
            RegisterAll();

            // 3) Hand off to the module for the bulk load.
            float start = Time.time;
            await _service.InitializeAsync();
            Debug.LogWarning("All Data Loaded. Total Time: " + (Time.time - start));
        }

        // ---------------------------------------------------------------
        // 1) Init / config table + force-update — stays game-specific.
        // ---------------------------------------------------------------
        private async UniTask LoadInitConfigAndMigration(ProjectConfig projectConfig)
        {
            const string clientMiscKey = "ClientMiscTable";

            // Register the init loader so we can reuse the same generic loader path.
            Register<ClientMiscTable, ClientMiscCollection>(
                DataToolLoadMode.Parallel,
                () => GameDataLocalFactory.Instance.ClientMiscTableLocal.Data);

            // Force-load it now (before everything else).
            string json = await _service.LoadSingleTable(clientMiscKey);
            await _service.GetLoader(clientMiscKey).LoadAsync(string.Empty, json, true);

            ClientMiscCollection misc = ServiceLocator.Instance.GetService<ClientMiscCollection>();
            if (misc != null)
            {
                // ...cheat/debug setup + MigrationController.RunBeforeLoadUserData() exactly as today...
                var migration = new MigrationController(projectConfig, misc.Data.forceUpdateData);
                await migration.RunBeforeLoadUserData();
            }
        }

        // ---------------------------------------------------------------
        // 2) Table registration — the single source of truth (table + phase + fallback + DI).
        // ---------------------------------------------------------------
        private void RegisterAll()
        {
            // Parallel (batched, concurrent)
            RegisterParallel<ShopDataTable, ShopDataCollection>(() => GameDataLocalFactory.Instance.ShopDataTableLocal.Data);
            RegisterParallel<IAPProductsTable, IAPProductsCollection>(() => GameDataLocalFactory.Instance.IapProductsTableLocal.Data);
            RegisterParallel<DailyRewardsTable, DailyRewardsCollection>(() => GameDataLocalFactory.Instance.DailyRewardsTableLocal.Data);
            RegisterParallel<InboxMessageTable, InboxMessageCollection>(() => GameDataLocalFactory.Instance.InboxMessageTableLocal.Data);
            RegisterParallel<CommunityRoulettePool, CommunityRoulettePoolCollection>(() => GameDataLocalFactory.Instance.CommunityRoulettePoolLocal.Data);
            RegisterParallel<CommunityRouletteTable, CommunityRouletteCollection>(() => GameDataLocalFactory.Instance.communityRouletteTableLocal.Data);
            RegisterParallel<BotDataTable, BotDataCollection>(() => GameDataLocalFactory.Instance.BotDataTableLocal.Data);
            RegisterParallel<MapCityTable, MapCityCollection>(() => GameDataLocalFactory.Instance.MapCityTableLocal.Data);
            RegisterParallel<AchievementTable, AchievementsCollection>(() => GameDataLocalFactory.Instance.AchievementTableLocal.Data);
            RegisterParallel<LevelRoxaneTable, LevelRoxaneCollection>(() => GameDataLocalFactory.Instance.LevelRoxaneTableLocal.Data);
            RegisterParallel<BakerPassTable, BakerPassCollection>(() => GameDataLocalFactory.Instance.BakerPassTableLocal.Data);
            RegisterParallel<LeaderBoardTable, LeaderBoardCollection>(() => GameDataLocalFactory.Instance.LeaderBoardTableLocal.Data);

            // Heavy (one-by-one)
            RegisterHeavy<LocalizeFRTable, LocalizeCollection>(() => GameDataLocalFactory.Instance.LocalizeFrTableLocal.Data);
            RegisterHeavy<CardsBoosterTable, CardsBoosterCollection>(() => GameDataLocalFactory.Instance.CardsBoosterTableLocal.Data);
            RegisterHeavy<GameplayLevelTable, GameplayLevelsCollection>(() => GameDataLocalFactory.Instance.GameplayLevelTableLocal.Data);
        }

        private void RegisterParallel<TData, TCollection>(Func<TData> fallback)
            where TData : class
            where TCollection : GameDataCollection<TData>, new()
            => Register<TData, TCollection>(DataToolLoadMode.Parallel, fallback);

        private void RegisterHeavy<TData, TCollection>(Func<TData> fallback)
            where TData : class
            where TCollection : GameDataCollection<TData>, new()
            => Register<TData, TCollection>(DataToolLoadMode.Heavy, fallback);

        private void Register<TData, TCollection>(DataToolLoadMode mode, Func<TData> fallback)
            where TData : class
            where TCollection : GameDataCollection<TData>, new()
        {
            // This Action is the seam: the module hands us data, we push it into ServiceLocator.
            Action<TData> registerAction = data =>
            {
                if (!ServiceLocator.Instance.TryGetService<TCollection>(out var service))
                {
                    service = new TCollection();
                    ServiceLocator.Instance.RegisterService(service);
                }

                service.InitData(data);
            };

            string tableKey = typeof(TData).Name; // matches Roxane's GameDataCollection.TableKey convention
            var loader = new GenericConfigDataToolLoader<TData>(tableKey, registerAction, fallback, _service);
            _service.RegisterTable(loader, mode);
        }
    }
}

#endif
