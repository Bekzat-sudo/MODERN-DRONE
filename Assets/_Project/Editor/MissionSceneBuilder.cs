using ModernDrone.Missions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ModernDrone.Editor
{
    /// <summary>Builds the first playable mission scene on the shared procedural terrain.</summary>
    public static class MissionSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Mission_01.unity";
        const string TerrainDataPath = "Assets/_Project/Scenes/Mission_01_Terrain.asset";

        internal const string Briefing =
            "Разведка обнаружила бронетехнику противника у лесополосы примерно в 330 м к северу " +
            "от точки взлёта (курс 000).\n\n" +
            "Задача: вывести из строя все три танка.\n\n" +
            "Лобовая броня закрыта динамической защитой — ищите борт, корму, крышу моторного отсека и башни. " +
            "Взрыватель взводится через 2 с после арма.";

        /// <summary>A mobile fire group hunting drones: not a mission target, but it can be struck.</summary>
        static GameObject Mog(Terrain terrain, string prefab, Vector3 pos, float yaw) =>
            ProvingGroundBuilder.PlacePrefab(terrain, prefab, pos, yaw, "МОГ");

        [MenuItem("Modern Drone/Setup/Build Mission 01 Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var terrain = ProvingGroundBuilder.CreateTerrain(TerrainDataPath);
            ProvingGroundBuilder.CreateLighting();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);

            // Targets parked along the tree line north of the launch site (tree line at z = 1300).
            var c = ProvingGroundBuilder.Center;
            Target(terrain, new Vector3(c.x - 40f, 0f, 1287f), 270f, "Танк №1 (бортом к нам)");
            Target(terrain, new Vector3(c.x + 60f, 0f, 1283f), 180f, "Танк №2 (лбом к нам)");
            Target(terrain, new Vector3(c.x + 150f, 0f, 1278f), 215f, "Танк №3 (на выезде)");

            Mog(terrain, VehicleCatalog.MogUral, new Vector3(c.x + 20f, 0f, 1335f), 190f);
            ProvingGroundBuilder.CreateMissionRig("Задача 1: Стоянка у посадки", Briefing, 4);

            EditorSceneManager.SaveScene(scene, ScenePath);
            ProvingGroundBuilder.AddToBuildSettings(ScenePath, 0);
            Debug.Log($"[ModernDrone] Mission scene built: {ScenePath}");
        }

        const string Scene02Path = "Assets/_Project/Scenes/Mission_02.unity";
        const string Terrain02Path = "Assets/_Project/Scenes/Mission_02_Terrain.asset";

        internal const string Briefing02 =
            "Два танка у той же лесополосы прикрыты станцией РЭБ: она стоит примерно в 280 м к северу от точки взлёта, " +
            "перед посадкой.\n\n" +
            "Рядом со станцией радиоканал почти не работает — видео в «снегу», управление запаздывает, возможен failsafe.\n\n" +
            "Задача: подавить станцию РЭБ (антенны на мачте или генератор в кунге) и вывести из строя оба танка. " +
            "Оптоволоконные дроны РЭБ не глушит, но их всего два.";

        [MenuItem("Modern Drone/Setup/Build Mission 02 Scene")]
        public static void BuildMission02()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var terrain = ProvingGroundBuilder.CreateTerrain(Terrain02Path);
            ProvingGroundBuilder.CreateLighting();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);

            var c = ProvingGroundBuilder.Center;
            var ewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleBuilder.EwStationPrefabPath) ?? VehicleBuilder.BuildEwStation();
            var ew = (GameObject)PrefabUtility.InstantiatePrefab(ewPrefab);
            var ewPos = new Vector3(c.x + 15f, 0f, 1240f);
            ewPos.y = terrain.SampleHeight(ewPos);
            ew.transform.SetPositionAndRotation(ewPos, Quaternion.Euler(0f, 100f, 0f));
            ew.name = "Станция РЭБ";
            ew.AddComponent<MissionTarget>().Configure("Станция РЭБ", TargetRequirement.Silenced);

            Target(terrain, new Vector3(c.x - 50f, 0f, 1286f), 270f, "Танк №1 (у посадки)");
            Target(terrain, new Vector3(c.x + 70f, 0f, 1283f), 200f, "Танк №2 (у посадки)");

            Mog(terrain, VehicleCatalog.MogZil, new Vector3(c.x - 15f, 0f, 1320f), 170f);
            ProvingGroundBuilder.CreateMissionRig("Задача 2: Прикрытие РЭБ", Briefing02, 3, 2);

            EditorSceneManager.SaveScene(scene, Scene02Path);
            ProvingGroundBuilder.AddToBuildSettings(Scene02Path, 2);
            Debug.Log($"[ModernDrone] Mission scene built: {Scene02Path}");
        }

        const string Scene03Path = "Assets/_Project/Scenes/Mission_03.unity";
        const string Terrain03Path = "Assets/_Project/Scenes/Mission_03_Terrain.asset";

        internal const string Briefing03 =
            "За лесополосой в ~350 м к северу — огневые позиции: САУ и две буксируемые гаубицы. " +
            "Их прикрывает расчёт дронов-перехватчиков.\n\n" +
            "Расчёт замечает дрон над посадкой и выше ~5 м и через пару секунд поднимает перехватчик. " +
            "Он быстрее, но разворачивается шире: уходите резким манёвром, ныряйте в лесополосу, держитесь низко. " +
            "Выбейте антенну на мачте или пост оператора — и перехватчики останутся без управления.\n\n" +
            "Разведка сообщает о возможных макетах техники. Осколочная БЧ эффективна по орудиям и расчёту, кумулятивная — по броне.";

        [MenuItem("Modern Drone/Setup/Build Mission 03 Scene")]
        public static void BuildMission03()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = ProvingGroundBuilder.CreateTerrain(Terrain03Path);
            ProvingGroundBuilder.CreateLighting();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);
            var c = ProvingGroundBuilder.Center;

            // North of the tree line (z = 1300): the SPAAG covers the gun line.
            TargetAt(terrain, VehicleCatalog.InterceptorSite, new Vector3(c.x + 30f, 0f, 1340f), 190f, "Расчёт перехватчиков", TargetRequirement.Silenced);
            TargetAt(terrain, VehicleCatalog.Spg, new Vector3(c.x - 70f, 0f, 1345f), 170f, "САУ", TargetRequirement.OutOfAction);
            TargetAt(terrain, VehicleCatalog.Howitzer, new Vector3(c.x + 110f, 0f, 1355f), 180f, "Гаубица №1", TargetRequirement.OutOfAction);
            TargetAt(terrain, VehicleCatalog.Howitzer, new Vector3(c.x + 135f, 0f, 1358f), 180f, "Гаубица №2", TargetRequirement.OutOfAction);
            // Their tow tractor waits behind the firing position.
            ProvingGroundBuilder.PlacePrefab(terrain, VehicleCatalog.Mtlb, new Vector3(c.x + 125f, 0f, 1385f), 90f, "МТ-ЛБМ");
            ProvingGroundBuilder.PlacePrefab(terrain, VehicleCatalog.Truck, new Vector3(c.x + 120f, 0f, 1378f), 90f, "Грузовик");
            // Bait south of the trees, in plain view of the launch site.
            ProvingGroundBuilder.PlacePrefab(terrain, VehicleCatalog.DecoyTank, new Vector3(c.x - 20f, 0f, 1286f), 160f, "Танк");

            Mog(terrain, VehicleCatalog.MogUral, new Vector3(c.x + 40f, 0f, 1385f), 180f);
            ProvingGroundBuilder.CreateMissionRig("Задача 3: Огневые позиции", Briefing03, 5, 1);

            EditorSceneManager.SaveScene(scene, Scene03Path);
            ProvingGroundBuilder.AddToBuildSettings(Scene03Path, 3);
            Debug.Log($"[ModernDrone] Mission scene built: {Scene03Path}");
        }

        static GameObject TargetAt(Terrain terrain, string prefab, Vector3 pos, float yaw, string label, TargetRequirement req)
        {
            var go = ProvingGroundBuilder.PlacePrefab(terrain, prefab, pos, yaw, label);
            go.AddComponent<MissionTarget>().Configure(label, req);
            return go;
        }

        const string Scene04Path = "Assets/_Project/Scenes/Mission_04.unity";
        const string Terrain04Path = "Assets/_Project/Scenes/Mission_04_Terrain.asset";

        internal const string Briefing04 =
            "По грунтовке за лесополосой, в ~500 м к северу, с запада на восток идёт колонна: бронеавтомобиль «Тайфун», БТР, грузовик, РСЗО, пусковая С-400 на марше и танк.\n\n" +
            "Задача: вывести из строя 3 машины из 6, пока колонна не ушла. Подбитая машина встаёт и задерживает остальных; " +
            "после первого попадания колонна ускоряется и ставит дым.\n\n" +
            "Два участка дороги закрыты противодроновыми сетками — в сетке дрон застревает и чаще всего не взрывается, но в ней есть разрывы. " +
            "В колонне идёт ЗИЛ с ДШК (мобильная огневая группа), дорогу прикрывает расчёт дронов-перехватчиков. Колонна тронется с вашим первым взлётом.";

        [MenuItem("Modern Drone/Setup/Build Mission 04 Scene")]
        public static void BuildMission04()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = ProvingGroundBuilder.CreateTerrain(Terrain04Path);
            ProvingGroundBuilder.CreateLighting();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);

            var road = new[]
            {
                new Vector3(260f, 0f, 1470f), new Vector3(600f, 0f, 1500f), new Vector3(1000f, 0f, 1515f),
                new Vector3(1400f, 0f, 1495f), new Vector3(1780f, 0f, 1465f)
            };
            ProvingGroundBuilder.CreateRoad(terrain, road);
            // Two netted stretches of the road.
            NetTunnelBuilder.Build(terrain, road, 430f, 590f, 5);
            NetTunnelBuilder.Build(terrain, road, 880f, 980f, 9);

            var column = new System.Collections.Generic.List<ModernDrone.Damage.VehicleDamageModel>();
            (string prefab, string label)[] order =
            {
                (VehicleCatalog.Mrap, "Тайфун"), (VehicleCatalog.Apc, "БТР"), (VehicleCatalog.Truck, "Грузовик"),
                (VehicleCatalog.Mlrs, "РСЗО"), (VehicleCatalog.SamLauncherTravel, "ПУ С-400"), (VehicleBuilder.TankPrefabPath, "Танк")
            };
            foreach (var (prefab, label) in order)
            {
                column.Add(TargetAt(terrain, prefab, road[0], 90f, label, TargetRequirement.OutOfAction).GetComponent<ModernDrone.Damage.VehicleDamageModel>());
                // The MOG rides third, covering the middle of the column.
                if (label == "БТР") column.Add(Mog(terrain, VehicleCatalog.MogZil, road[0], 90f).GetComponent<ModernDrone.Damage.VehicleDamageModel>());
            }

            var convoy = new GameObject("Convoy").AddComponent<ModernDrone.Vehicles.Convoy>();
            convoy.Configure(road, column, 7f);

            ProvingGroundBuilder.PlacePrefab(terrain, VehicleCatalog.InterceptorSite, new Vector3(1060f, 0f, 1580f), 200f, "Расчёт перехватчиков");

            var mission = ProvingGroundBuilder.CreateMissionRig("Задача 4: Колонна", Briefing04, 5, 1);
            mission.SetRequiredTargets(3);

            EditorSceneManager.SaveScene(scene, Scene04Path);
            ProvingGroundBuilder.AddToBuildSettings(Scene04Path, 4);
            Debug.Log($"[ModernDrone] Mission scene built: {Scene04Path}");
        }


        internal const string Briefing05 =
            "За второй лесополосой, в ~800 м к северу, развернута позиция ЗРК: РЛС, две пусковые и прикрывающий их ЗРПК.\n\n" +
            "Подход глушит станция РЭБ, над позицией дежурит расчёт дронов-перехватчиков. " +
            "Оптоволокно не глушится, но катушки хватит только на прямой маршрут.\n\n" +
            "Задача: вывести из строя 3 цели из 5 (РЛС и РЭБ — подавить, пусковые и ЗРПК — вывести из строя).";

        [MenuItem("Modern Drone/Setup/Build Mission 05 Scene")]
        public static void BuildMission05()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = ProvingGroundBuilder.CreateTerrain("Assets/_Project/Scenes/Mission_05_Terrain.asset");
            ProvingGroundBuilder.CreateLighting();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);

            TargetAt(terrain, VehicleCatalog.SamRadar, new Vector3(1000f, 0f, 1760f), 180f, "РЛС ЗРК", TargetRequirement.Silenced);
            TargetAt(terrain, VehicleCatalog.SamLauncher, new Vector3(960f, 0f, 1790f), 170f, "Пусковая №1", TargetRequirement.OutOfAction);
            TargetAt(terrain, VehicleCatalog.SamLauncher, new Vector3(1045f, 0f, 1795f), 190f, "Пусковая №2", TargetRequirement.OutOfAction);
            TargetAt(terrain, VehicleCatalog.Spaag, new Vector3(1010f, 0f, 1720f), 180f, "ЗРПК", TargetRequirement.OutOfAction);
            TargetAt(terrain, VehicleBuilder.EwStationPrefabPath, new Vector3(930f, 0f, 1700f), 90f, "Станция РЭБ", TargetRequirement.Silenced);
            ProvingGroundBuilder.PlacePrefab(terrain, VehicleCatalog.InterceptorSite, new Vector3(1090f, 0f, 1740f), 200f, "Расчёт перехватчиков");

            Mog(terrain, VehicleCatalog.MogUral, new Vector3(1070f, 0f, 1700f), 200f);
            var mission = ProvingGroundBuilder.CreateMissionRig("Задача 5: Позиция ЗРК", Briefing05, 3, 3);
            mission.SetRequiredTargets(3);
            EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/Mission_05.unity");
            ProvingGroundBuilder.AddToBuildSettings("Assets/_Project/Scenes/Mission_05.unity", 5);
        }

        internal const string Briefing06 =
            "Вдоль лесополосы в ~330 м к северу стоит техника: разведка насчитала восемь машин, но большинство — надувные макеты.\n\n" +
            "Настоящих три: САУ, РСЗО и грузовик. Макеты блестят, у них упрощённые формы и лёгкий крен. Позицию прикрывает мобильная огневая группа.\n\n" +
            "Задача: вывести из строя все три настоящие машины. Каждый удар по макету — потерянный дрон.";

        [MenuItem("Modern Drone/Setup/Build Mission 06 Scene")]
        public static void BuildMission06()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = ProvingGroundBuilder.CreateTerrain("Assets/_Project/Scenes/Mission_06_Terrain.asset");
            ProvingGroundBuilder.CreateLighting();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);

            TargetAt(terrain, VehicleCatalog.Spg, new Vector3(870f, 0f, 1287f), 250f, "САУ", TargetRequirement.OutOfAction);
            TargetAt(terrain, VehicleCatalog.Mlrs, new Vector3(1130f, 0f, 1288f), 100f, "РСЗО", TargetRequirement.OutOfAction);
            TargetAt(terrain, VehicleCatalog.Truck, new Vector3(1210f, 0f, 1290f), 90f, "Грузовик", TargetRequirement.OutOfAction);
            (string prefab, float x, float yaw)[] decoys =
            {
                (VehicleCatalog.DecoyTank, 820f, 90f), (VehicleCatalog.DecoyMlrs, 960f, 260f), (VehicleCatalog.DecoySpaag, 1040f, 80f),
                (VehicleCatalog.DecoyTank, 1280f, 280f), (VehicleCatalog.DecoyMlrs, 760f, 100f)
            };
            foreach (var (prefab, x, yaw) in decoys)
                ProvingGroundBuilder.PlacePrefab(terrain, prefab, new Vector3(x, 0f, 1288f), yaw);

            Mog(terrain, VehicleCatalog.MogZil, new Vector3(1150f, 0f, 1330f), 180f);
            ProvingGroundBuilder.CreateMissionRig("Задача 6: Ложные цели", Briefing06, 5);
            EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/Mission_06.unity");
            ProvingGroundBuilder.AddToBuildSettings("Assets/_Project/Scenes/Mission_06.unity", 6);
        }

        internal const string Briefing07 =
            "Ночь. Техника противника стоит у лесополосы в ~330 м к северу. Дроны оснащены тепловизорами " +
            "(384x288, узкий угол обзора, «белое — горячее»): тёплые моторы видны издалека, холодная земля почти чёрная.\n\n" +
            "Среди целей есть надувные макеты — они холодные и на тепловизоре не светятся.\n\n" +
            "Задача: вывести из строя танк, РСЗО и грузовик. За лесополосой дежурит расчёт перехватчиков.";

        [MenuItem("Modern Drone/Setup/Build Mission 07 Scene")]
        public static void BuildMission07()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = ProvingGroundBuilder.CreateTerrain("Assets/_Project/Scenes/Mission_07_Terrain.asset");
            ProvingGroundBuilder.CreateLighting();
            MakeNight();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);

            var heat = HeatMaterial();
            var c = ProvingGroundBuilder.Center;
            foreach (var (prefab, pos, yaw, label) in new[]
            {
                (VehicleBuilder.TankPrefabPath, new Vector3(c.x - 40f, 0f, 1287f), 270f, "Танк"),
                (VehicleCatalog.Mlrs, new Vector3(c.x + 60f, 0f, 1285f), 100f, "РСЗО"),
                (VehicleCatalog.Truck, new Vector3(c.x + 150f, 0f, 1280f), 215f, "Грузовик")
            })
                TargetAt(terrain, prefab, pos, yaw, label, TargetRequirement.OutOfAction)
                    .AddComponent<ModernDrone.Vehicles.HeatSignature>().Configure(heat);

            ProvingGroundBuilder.PlacePrefab(terrain, VehicleCatalog.DecoyTank, new Vector3(c.x - 100f, 0f, 1288f), 90f);
            ProvingGroundBuilder.PlacePrefab(terrain, VehicleCatalog.DecoyMlrs, new Vector3(c.x + 100f, 0f, 1288f), 260f);
            var site = ProvingGroundBuilder.PlacePrefab(terrain, VehicleCatalog.InterceptorSite, new Vector3(c.x, 0f, 1360f), 180f, "Расчёт перехватчиков");
            site.AddComponent<ModernDrone.Vehicles.HeatSignature>().Configure(heat);

            Mog(terrain, VehicleCatalog.MogUral, new Vector3(c.x + 20f, 0f, 1335f), 190f).AddComponent<ModernDrone.Vehicles.HeatSignature>().Configure(heat);
            var mission = ProvingGroundBuilder.CreateMissionRig("Задача 7: Ночная охота", Briefing07, 4);
            mission.gameObject.AddComponent<ThermalNight>();
            EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/Mission_07.unity");
            ProvingGroundBuilder.AddToBuildSettings("Assets/_Project/Scenes/Mission_07.unity", 7);
        }

        internal const string Briefing08 =
            "Ударный вертолёт Ми-28НМ работает по нашим позициям: низко переходит между огневыми точками " +
            "в 0,8–1 км к северу и на каждой зависает на ~20 секунд, чтобы отстреляться.\n\n" +
            "Догнать его на переходе не выйдет — ловите на зависании. Уязвимые места: хвостовой винт и хвостовая балка, " +
            "двигатели над крыльями, несущий винт. Кабина бронирована.\n\n" +
            "Задача: сбить Ми-28НМ.";

        [MenuItem("Modern Drone/Setup/Build Mission 08 Scene")]
        public static void BuildMission08()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = ProvingGroundBuilder.CreateTerrain("Assets/_Project/Scenes/Mission_08_Terrain.asset");
            ProvingGroundBuilder.CreateLighting();
            ProvingGroundBuilder.CreateTreeLines(terrain);
            ProvingGroundBuilder.CreateSpawn(terrain);

            var points = new[] { new Vector3(820f, 0f, 1750f), new Vector3(1150f, 0f, 1850f), new Vector3(1350f, 0f, 1650f) };
            var heli = TargetAt(terrain, VehicleCatalog.Helicopter, points[0], 200f, "Ми-28НМ", TargetRequirement.OutOfAction);
            heli.transform.position += Vector3.up * 35f;
            var flight = heli.GetComponent<ModernDrone.Vehicles.HelicopterFlight>();
            flight.Configure(points, heli.transform.Find("Rotor"));
            EditorUtility.SetDirty(flight);

            Mog(terrain, VehicleCatalog.MogZil, new Vector3(1000f, 0f, 1720f), 200f);
            ProvingGroundBuilder.CreateMissionRig("Задача 8: Охота на вертолёт", Briefing08, 4);
            EditorSceneManager.SaveScene(scene, "Assets/_Project/Scenes/Mission_08.unity");
            ProvingGroundBuilder.AddToBuildSettings("Assets/_Project/Scenes/Mission_08.unity", 8);
        }

        /// <summary>Moonlit night: dim bluish key light, almost no ambient, no sky.</summary>
        static void MakeNight()
        {
            var sun = GameObject.Find("Sun").GetComponent<Light>();
            sun.intensity = 0.035f;
            sun.color = new Color(0.6f, 0.7f, 1f);
            sun.shadows = LightShadows.None;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.015f, 0.015f, 0.018f);
            RenderSettings.fogColor = new Color(0.005f, 0.005f, 0.01f);
            RenderSettings.fogDensity = 0.002f;
            // The ambient probe is otherwise still the daytime sky: rebuild it from the night ambient colour.
            var probe = new UnityEngine.Rendering.SphericalHarmonicsL2();
            probe.AddAmbientLight(RenderSettings.ambientLight);
            RenderSettings.ambientProbe = probe;
            RenderSettings.reflectionIntensity = 0f;
        }

        static Material HeatMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/HeatUnlit.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "HeatUnlit", enableInstancing = true };
            mat.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
        static void Target(Terrain terrain, Vector3 pos, float yaw, string label)
        {
            var tank = ProvingGroundBuilder.PlaceTank(terrain, pos, yaw, label);
            tank.AddComponent<MissionTarget>().Configure(label, TargetRequirement.OutOfAction);
        }
    }
}
