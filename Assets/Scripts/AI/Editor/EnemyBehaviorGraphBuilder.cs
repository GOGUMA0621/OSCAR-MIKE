using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OskarMike.AI.Editor
{
    // Behavior 1.0.16 authoring API is internal. Keep reflection confined to this editor adapter.
    // Generated assets remain ordinary editable Behavior graphs; never rebuild them automatically.
    public static class EnemyBehaviorGraphBuilder
    {
        public const string MainPath = "Assets/AI/EnemyBehavior.asset";
        private const string Folder = "Assets/AI/Behaviors";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null)
            ?? throw new InvalidOperationException(name);
        private static object Get(object o, string name) => o.GetType().GetProperty(name, Flags)?.GetValue(o) ?? o.GetType().GetField(name, Flags)?.GetValue(o);
        private static void Set(object o, string name, object value)
        {
            var property = o.GetType().GetProperty(name, Flags);
            if (property != null) property.SetValue(o, value); else o.GetType().GetField(name, Flags).SetValue(o, value);
        }
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethods(Flags)
            .First(m => m.Name == name && !m.IsGenericMethod && m.GetParameters().Length == args.Length).Invoke(o, args);

        public static readonly (string name, float value)[] Tuning =
        {
            ("PatrolSpeed", 2), ("ChaseSpeed", 4), ("SightRange", 12), ("FiringRange", 10),
            ("FieldOfView", 100), ("SenseInterval", .1f), ("AlertDuration", 1.5f), ("SignalProgress", .8f),
            ("FireInterval", 1.5f), ("WaypointWait", 1), ("InvestigationWait", 2),
            ("EyeHeight", 1.6f), ("TurnSpeed", 360), ("PathRefreshInterval", .2f),
            ("DestinationTolerance", .5f), ("NavMeshSampleRadius", 1.5f), ("ArrivalTolerance", .15f)
        };

        public static BehaviorGraph EnsureGraph()
        {
            var asset = AssetDatabase.LoadAssetAtPath(MainPath, TypeOf("Unity.Behavior.BehaviorAuthoringGraph"));
            if (asset != null && ((IEnumerable)Get(Get(asset, "Blackboard"), "Variables")).Cast<object>().Any(v => (string)Get(v, "Name") == "PatrolSpeed"))
                return (BehaviorGraph)Call(asset, "BuildRuntimeGraph", true)
                    ?? throw new InvalidOperationException("일반 몹 그래프를 컴파일하지 못했습니다.");
            return Migrate();
        }

        [MenuItem("Tools/OSKAR MIKE/AI/일반 몹 그래프 구성")]
        public static void Configure() { EnsureGraph(); AssetDatabase.SaveAssets(); }

        // Presentation-only: does not replace nodes, edges, conditions or variable values.
        public static void ConfigurePresentation()
        {
            foreach (var entry in new[] { (MainPath, "일반 몹 행동 선택"), (Folder + "/Patrol.asset", "순찰"),
                (Folder + "/Investigate.asset", "조사"), (Folder + "/Alert.asset", "경고 및 증원"), (Folder + "/Combat.asset", "추격 및 사격") })
            {
                var asset = AssetDatabase.LoadAssetAtPath(entry.Item1, TypeOf("Unity.Behavior.BehaviorAuthoringGraph"));
                if (asset == null) continue;
                Set(Get(asset, "Story"), "Story", entry.Item2);
                foreach (var node in ((IList)Get(asset, "Nodes")).Cast<object>())
                    if (node.GetType().Name == "SubgraphNodeModel") Set(node, "ShowStaticSubgraphRepresentation", true);
                var list = (IList)Get(Get(asset, "Blackboard"), "Variables");
                var settings = Tuning.Select(t => t.name).Concat(new[] { "CanRequestReinforcements" }).ToArray();
                var ordered = list.Cast<object>().OrderBy(v =>
                {
                    int index = Array.IndexOf(settings, (string)Get(v, "Name"));
                    return index < 0 ? settings.Length : index;
                }).ToArray();
                list.Clear(); foreach (var variable in ordered) list.Add(variable);
                Call(asset, "SetAssetDirty", false);
                EditorUtility.SetDirty((Object)Get(asset, "Blackboard"));
            }
            AssetDatabase.SaveAssets();
        }

        public static BehaviorGraph Migrate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("플레이 모드를 종료한 뒤 구성하세요.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/AI", "Behaviors");
            var patrol = new Graph(Folder + "/Patrol.asset", false);
            var investigate = new Graph(Folder + "/Investigate.asset", false);
            var alert = new Graph(Folder + "/Alert.asset", false);
            var combat = new Graph(Folder + "/Combat.asset", false);
            BuildPatrol(patrol); BuildInvestigation(investigate); BuildAlert(alert); BuildCombat(combat);
            patrol.Build(); investigate.Build(); alert.Build(); combat.Build();

            var main = new Graph(MainPath, true);
            var selector = main.Node("SelectorComposite", "CompositeNodeModel", 600, 170);
            main.Connect(main.Start, selector);
            var warning = main.Guard(selector, 0, 350, ("HasVisual", true), ("Engaged", false), ("CanRequestReinforcements", true));
            main.Subgraph(warning, alert, 0, 570);
            var fighting = main.Guard(selector, 400, 350, ("HasVisual", true));
            main.Subgraph(fighting, combat, 400, 570);
            var searching = main.Guard(selector, 800, 350, ("HasClue", true));
            main.Subgraph(searching, investigate, 800, 570);
            main.Subgraph(selector, patrol, 1200, 350);
            var runtime = main.Build();
            // Assign the same runtime subasset without rebuilding the scene or replacing the prefab.
            const string prefabPath = "Assets/AI/Prefabs/PatrolEnemy.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    prefab.GetComponent<BehaviorGraphAgent>().Graph = runtime;
                    PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            AssetDatabase.SaveAssets();
            ConfigurePresentation();
            Selection.activeObject = main.Asset;
            Debug.Log("[적 AI] 일반 몹 그래프와 Blackboard 구성이 완료되었습니다.");
            return runtime;
        }

        private static void BuildPatrol(Graph g)
        {
            var sequence = g.Sequence(g.Start, 400, 160);
            g.State(sequence, EnemyState.Patrol, 0, 350);
            var attempt = g.Node("SucceederModifier", "ModifierNodeModel", 350, 350); g.Connect(sequence, attempt);
            var movement = g.Sequence(attempt, 350, 530);
            var pick = g.Action<EnemyPickPatrolPoint>(movement, 100, 720); g.Link(pick, "Destination", "PatrolDestination");
            var move = g.Action<EnemyMoveTo>(movement, 550, 720); g.Link(move, "Destination", "PatrolDestination"); g.Link(move, "Speed", "PatrolSpeed");
            g.Wait(sequence, "WaypointWait", 800, 350);
            g.Action<EnemyAdvancePatrol>(sequence, 1150, 350);
        }
        private static void BuildInvestigation(Graph g)
        {
            var restart = g.Node("RestartModifier", "AbortNodeModel", 400, 160); g.Connect(g.Start, restart);
            g.AddChangedCondition(restart);
            var root = g.Sequence(restart, 400, 340);
            var capture = g.Action<EnemyCaptureClue>(root, 0, 530);
            g.Link(capture, "Destination", "InvestigationDestination"); g.Link(capture, "Revision", "InvestigatedRevision");
            g.State(root, EnemyState.Investigate, 400, 530);
            var attempt = g.Node("SucceederModifier", "ModifierNodeModel", 800, 530); g.Connect(root, attempt);
            var search = g.Sequence(attempt, 800, 710);
            var move = g.Action<EnemyMoveTo>(search, 600, 890);
            g.Link(move, "Destination", "InvestigationDestination"); g.Link(move, "Speed", "ChaseSpeed");
            g.Wait(search, "InvestigationWait", 1000, 890);
            var clear = g.Action<EnemyClearClue>(root, 1250, 530); g.Link(clear, "Revision", "InvestigatedRevision");
        }
        private static void BuildAlert(Graph g)
        {
            var sequence = g.Sequence(g.Start, 500, 160);
            g.Action<EnemyBeginAlert>(sequence, 0, 370);
            var signal = g.Action<EnemyWaitAlert>(sequence, 350, 370);
            g.Link(signal, "Duration", "AlertDuration"); g.Link(signal, "Progress", "SignalProgress");
            g.Action<EnemyRequestReinforcement>(sequence, 750, 370);
            var finish = g.Action<EnemyWaitAlert>(sequence, 1100, 370); g.Link(finish, "Duration", "AlertDuration");
            g.Action<EnemyCompleteAlert>(sequence, 1500, 370);
        }
        private static void BuildCombat(Graph g)
        {
            var root = g.Sequence(g.Start, 550, 160);
            g.Action<EnemyBeginEngagement>(root, 0, 350);
            var abort = g.Abort(root, 600, 350, ("HasVisual", false));
            var repeat = g.Node("RepeaterModifier", "RepeatNodeModel", 600, 530); g.Connect(abort, repeat);
            var choice = g.Node("SelectorComposite", "CompositeNodeModel", 600, 710); g.Connect(repeat, choice);
            var attackGuard = g.Guard(choice, 0, 900, ("InFiringRange", true), ("AttackAllowed", true));
            var shoot = g.Sequence(attackGuard, 0, 1080);
            g.State(shoot, EnemyState.Shoot, -250, 1260);
            g.Action<EnemyAim>(shoot, 100, 1260);
            var fireAttempt = g.Node("SucceederModifier", "ModifierNodeModel", 450, 1260); g.Connect(shoot, fireAttempt);
            g.Action<EnemyFire>(fireAttempt, 450, 1440);
            var aimGuard = g.Guard(choice, 650, 900, ("InFiringRange", true));
            var aim = g.Sequence(aimGuard, 650, 1080);
            g.State(aim, EnemyState.Chase, 700, 1260); g.Action<EnemyAim>(aim, 1050, 1260);
            var chase = g.Sequence(choice, 1300, 900);
            g.State(chase, EnemyState.Chase, 1300, 1080);
            var follow = g.Action<EnemyFollowTarget>(chase, 1700, 1080); g.Link(follow, "Speed", "ChaseSpeed");
        }

        private sealed class Graph
        {
            public Object Asset;
            public object Start;
            public BehaviorGraph Runtime;
            private readonly Dictionary<string, object> variables = new();
            public Graph(string path, bool main)
            {
                var type = TypeOf("Unity.Behavior.BehaviorAuthoringGraph");
                Asset = AssetDatabase.LoadAssetAtPath(path, type);
                if (Asset == null) { Asset = ScriptableObject.CreateInstance(type); Asset.name = System.IO.Path.GetFileNameWithoutExtension(path); AssetDatabase.CreateAsset(Asset, path); }
                Call(Asset, "EnsureAssetHasBlackboard");
                foreach (var node in ((IList)Get(Asset, "Nodes")).Cast<object>().ToArray())
                    if (((IList)Get(Asset, "Nodes")).Contains(node)) Call(Asset, "DeleteNode", node);
                var board = Get(Asset, "Blackboard");
                var list = (IList)Get(board, "Variables");
                foreach (var variable in list.Cast<object>()) variables[(string)Get(variable, "Name")] = variable;
                foreach (var item in Tuning) Variable(item.name, typeof(float), item.value, true);
                Variable("CanRequestReinforcements", typeof(bool), true, true);
                foreach (var name in new[] { "HasVisual", "HasClue", "Engaged", "AlertInProgress", "InFiringRange", "FireReady", "AttackAllowed" }) Variable(name, typeof(bool), false, !main);
                Variable("Target", typeof(GameObject), null, !main);
                foreach (var name in new[] { "LastKnownPosition", "PatrolDestination", "InvestigationDestination" }) Variable(name, typeof(Vector3), Vector3.zero, !main);
                foreach (var name in new[] { "PatrolIndex", "ClueRevision", "InvestigatedRevision" }) Variable(name, typeof(int), 0, !main);
                Variable("FireAllowedAt", typeof(float), 0f, !main);
                Start = Node("Start", "StartNodeModel", 500, 0); Set(Start, "Repeat", main);
                Call(board, "SetAssetDirty");
            }
            private void Variable(string name, Type type, object value, bool exposed)
            {
                if (!variables.TryGetValue(name, out var model))
                {
                    model = MakeVariable(type, value); Set(model, "Name", name);
                    ((IList)Get(Get(Asset, "Blackboard"), "Variables")).Add(model); variables.Add(name, model);
                }
                Set(model, "IsExposed", exposed); Set(model, "IsShared", false);
            }
            private static object MakeVariable(Type type, object value)
            {
                var model = Activator.CreateInstance(TypeOf("Unity.Behavior.GraphFramework.TypedVariableModel`1").MakeGenericType(type));
                Set(model, "ObjectValue", value); return model;
            }
            public object Node(string name, string model, float x, float y) => Node(TypeOf("Unity.Behavior." + name), model, x, y);
            private object Node(Type type, string model, float x, float y)
            {
                var info = TypeOf("Unity.Behavior.NodeRegistry").GetMethod("GetInfo", Flags).Invoke(null, new object[] { type });
                if (info == null) throw new InvalidOperationException("Node not registered: " + type.Name);
                return Call(Asset, "CreateNode", TypeOf("Unity.Behavior." + model), new Vector2(x, y), null, new[] { info });
            }
            public object Action<T>(object parent, float x, float y)
            { var node = Node(typeof(T), "ActionNodeModel", x, y); Connect(parent, node); return node; }
            public object Sequence(object parent, float x, float y)
            { var node = Node("SequenceComposite", "CompositeNodeModel", x, y); Connect(parent, node); return node; }
            public void Connect(object parent, object child)
            {
                var output = ((IEnumerable)Get(parent, "OutputPortModels")).Cast<object>().First();
                var input = ((IEnumerable)Get(child, "InputPortModels")).Cast<object>().First();
                Call(Asset, "ConnectEdge", output, input);
            }
            public void Link(object node, string field, string variable)
            {
                var model = variables[variable];
                Call(node, "SetField", field, model, (Type)Get(model, "Type"));
            }
            private static void Literal(object node, string field, Type type, object value)
            { var f = Call(node, "GetOrCreateField", field, type); ((BlackboardVariable)Get(f, "LocalValue")).ObjectValue = value; }
            public void State(object parent, EnemyState state, float x, float y) => Literal(Action<EnemySetState>(parent, x, y), "State", typeof(EnemyState), state);
            public void Wait(object parent, string seconds, float x, float y)
            { var node = Node("WaitAction", "ActionNodeModel", x, y); Connect(parent, node); Link(node, "SecondsToWait", seconds); }
            public object Guard(object parent, float x, float y, params (string name, bool value)[] conditions)
            {
                var node = Node("ConditionalGuardModifier", "ConditionalGuardNodeModel", x, y); Connect(parent, node);
                Set(node, "RequiresAllConditionsTrue", true);
                Set(node, "ObserverType", Enum.Parse(TypeOf("Unity.Behavior.ObserverAbortTarget"), "LowerPriority"));
                foreach (var condition in conditions) Flag(node, condition.name, condition.value);
                return node;
            }
            public object Abort(object parent, float x, float y, params (string name, bool value)[] conditions)
            {
                var node = Node("AbortModifier", "AbortNodeModel", x, y); Connect(parent, node);
                Set(node, "RequiresAllConditionsTrue", false);
                foreach (var condition in conditions) Flag(node, condition.name, condition.value);
                return node;
            }
            private object Condition(object node, Type type)
            {
                var info = TypeOf("Unity.Behavior.ConditionUtility").GetMethod("GetInfoForConditionType", Flags).Invoke(null, new object[] { type });
                var model = Activator.CreateInstance(TypeOf("Unity.Behavior.ConditionModel"), Flags, null,
                    new[] { node, Activator.CreateInstance(type), info }, null);
                ((IList)Get(node, "ConditionModels")).Add(model); return model;
            }
            private void Flag(object node, string name, bool expected)
            {
                var condition = Condition(node, typeof(EnemyFlagCondition)); Link(condition, "Value", name);
                Literal(condition, "Expected", typeof(bool), expected);
            }
            public void AddChangedCondition(object node)
            {
                var condition = Condition(node, typeof(EnemyClueChangedCondition));
                Link(condition, "Current", "ClueRevision"); Link(condition, "Captured", "InvestigatedRevision");
            }
            public void Subgraph(object parent, Graph child, float x, float y)
            {
                var node = Node("RunSubgraph", "SubgraphNodeModel", x, y); Connect(parent, node);
                Call(node, "SetField", "Subgraph", MakeVariable(typeof(BehaviorGraph), child.Runtime), typeof(BehaviorGraph));
                Call(node, "CacheRuntimeGraphId");
                Set(node, "ShowStaticSubgraphRepresentation", true);
                foreach (var pair in child.variables)
                    if ((bool)Get(pair.Value, "IsExposed") && variables.ContainsKey(pair.Key)) Link(node, pair.Key, pair.Key);
            }
            public BehaviorGraph Build()
            {
                Call(Asset, "SetAssetDirty", true);
                Runtime = (BehaviorGraph)Call(Asset, "BuildRuntimeGraph", true);
                if (Runtime == null) throw new InvalidOperationException("그래프 빌드 실패: " + Asset.name);
                EditorUtility.SetDirty(Asset); AssetDatabase.SaveAssets(); return Runtime;
            }
        }
    }
}
