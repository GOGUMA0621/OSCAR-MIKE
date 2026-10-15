using System;
using System.Linq;
using OskarMike.Testing;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OskarMike.EditorTools
{
    public static class RangeCourseSceneBuilder
    {
        const string Path = "Assets/Scenes/RangeCourse.unity";
        const string Materials = "Assets/Materials/RangeCourse";
        static Material concrete, range, track, steel, white, blue, dark;
        static Transform group;

        [MenuItem("Oskar Mike/Testing/Create Range and Obstacle Course")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Path) != null)
                throw new InvalidOperationException("RangeCourse already exists. Rename or move it before rebuilding to preserve scene edits.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            concrete = Mat("Concrete", new Color(.35f,.38f,.39f));
            range = Mat("RangeGreen", new Color(.20f,.34f,.22f));
            track = Mat("CourseRed", new Color(.48f,.16f,.12f));
            steel = Mat("Fence", new Color(.59f,.63f,.64f));
            white = Mat("Markings", new Color(.91f,.91f,.80f));
            blue = Mat("Obstacles", new Color(.10f,.32f,.65f));
            dark = Mat("Cover", new Color(.12f,.14f,.15f));
            Group("Ground and separation");
            Box("Range floor", -18,-.15f,158, 32,.3f,310,range);
            Box("Course apron",24,-.15f,31,46,.3f,66,concrete);
            Box("Shared starting apron",5,-.15f,-2,78,.3f,10,concrete);
            Box("Range separation wall 4m tall 0.6m thick",0,2,158,.6f,4,310,concrete);
            Box("Range left boundary",-34,2,158,.4f,4,310,concrete);
            Box("Range rear backstop",-17,3,312,34,6,1,concrete);
            // All distances are measured from the firing line z=8.
            Group("Shooting range");
            float[] xs = {-31,-27,-23,-19,-15,-10,-5};
            float[] distances = {30,30,30,30,100,200,300};
            for(int i=0;i<xs.Length;i++)
            {
                float x=xs[i], d=distances[i];
                Box("Firing line",x,.012f,8,3.5f,.024f,.12f,white,false);
                Box("Lane marking",x-1.85f,.012f,158,.045f,.024f,300,white,false);
                Target(x,8+d,i+1);
                Sign((i+1)+" / "+d+" m",x,2.5f,6);
                for(int m=5;m<=d;m+=5)
                {
                    Box("Distance tick "+m+"m",x,.015f,8+m,m%10==0?3.5f:1,.025f,.06f,white,false);
                    if(m%10==0) GroundText(m+"",x,8+m-1);
                }
            }
            Box("Seated rectangular cover 1x1x1.1m",-31,.55f,9.5f,1,1.1f,1,dark);
            Box("Standing rectangular cover 1x1x1.6m",-27,.8f,9.5f,1,1.6f,1,dark);
            Pillar("Seated cylindrical cover 1.1m",-23,9.5f,1.1f);
            Pillar("Standing cylindrical cover 1.6m",-19,9.5f,1.6f);
            Group("Obstacle courses");
            Lane(6,50,"1 / 50 m");
            Lane(14,50,"2 / 50 m");
            Lane(22,25,"3 / 25 m");
            Lane(32,45,"4 / 90 m");
            Lane(40,45,"4 / RETURN");
            Box("U bend",36,.015f,55,14,.03f,6,track,false);
            // Course 1 is intentionally an unobstructed movement baseline.
            for(int z=13;z<=53;z+=5)
                Box("Course 2 obstacle "+(z-8)+"m",14,.4f,z,5,.8f,.35f,blue);
            // Continuous underside clearance: 0.8m above ground, with open side visibility.
            Box("Crawl overhead",22,.86f,20.5f,5,.12f,25,steel);
            for(int z=8;z<=33;z+=5)
                foreach(float x in new[]{19.5f,24.5f}) Box("Crawl support",x,.4f,z,.10f,.8f,.1f,steel);
            float[] heights={1.5f,1f,.8f,.5f};
            for(int i=0;i<4;i++) Box("Parkour "+heights[i]+"m",32,heights[i]/2,16+i*9,5,heights[i],.4f,blue);
            foreach(float x in new[]{2.5f,9.5f,17.5f,26.5f,35.5f,43.5f})
                Fence(x,8,x==35.5f?50:58);
            // End fences leave a six-metre turning bay around the centre divider.
            Sign("1",6,2.4f,7); Sign("2",14,2.4f,7); Sign("3",22,2.4f,7); Sign("4",32,2.4f,7);
            Group("Scene setup");
            var sun=new GameObject("Sun").AddComponent<Light>();
            sun.type=LightType.Directional; sun.intensity=1.4f;
            sun.transform.rotation=Quaternion.Euler(48,-35,0);
            RenderSettings.ambientLight=new Color(.6f,.65f,.7f);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.fog=false;
            var camera=new GameObject("Overview Camera (disabled during play)").AddComponent<Camera>();
            camera.transform.position=new Vector3(105,160,-100);
            camera.transform.LookAt(new Vector3(0,0,130)); camera.farClipPlane=1000; camera.enabled=false;
            CopyNetwork(scene);
            new GameObject("RangeCourse Auto Host").AddComponent<MovementTestAutoHost>();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene,Path)) throw new Exception("Scene save failed");
            AssetDatabase.SaveAssets();
            Debug.Log("RANGE_COURSE_BUILD_OK "+Path);
        }
        static Material Mat(string name,Color color)
        {
            System.IO.Directory.CreateDirectory(Materials);
            string path=Materials+"/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m!=null) return m;
            m=new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color=color;
            AssetDatabase.CreateAsset(m,path); return m;
        }
        static void Group(string name) { group=new GameObject(name).transform; }
        static GameObject Box(string name,float x,float y,float z,float sx,float sy,float sz,Material mat,bool solid=true)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(group);
            g.transform.position=new Vector3(x,y,z);g.transform.localScale=new Vector3(sx,sy,sz);
            g.GetComponent<Renderer>().sharedMaterial=mat;
            if(!solid) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }
        static void Pillar(string name,float x,float z,float height)
        {
            var g=GameObject.CreatePrimitive(PrimitiveType.Cylinder);g.name=name;g.transform.SetParent(group);
            g.transform.position=new Vector3(x,height/2,z);g.transform.localScale=new Vector3(1,height/2,1);
            g.GetComponent<Renderer>().sharedMaterial=dark;
        }
        static void Target(float x,float z,int lane)
        {
            Box("Target "+lane,x,1.55f,z,.65f,1,.12f,white);
            Box("Target centre",x,1.55f,z-.07f,.22f,.35f,.015f,dark,false);
            Box("Target head",x,2.18f,z,.3f,.3f,.12f,white);
            Box("Target stand",x,.5f,z,.12f,1,.12f,steel);
            Box("Target backstop",x,2,z+2,3.8f,4,.5f,concrete);
        }
        static void Lane(float x,float length,string label)
        {
            Box(label,x,.012f,8+length/2,6,.024f,length,track,false);
            for(int m=0;m<=length;m++)
                Box("Metre "+m,x,.03f,8+m,m%5==0?5.6f:1,.015f,.035f,white,false);
            for(int m=5;m<=length;m+=5) GroundText(m+"",x,8+m-.6f);
        }
        static void Fence(float x,float from,float to)
        {
            for(float z=from;z<=to;z+=2) Box("Fence post",x,.55f,z,.08f,1.1f,.08f,steel);
            foreach(float y in new[]{.45f,1.05f}) Box("Open fence rail",x,y,(from+to)/2,.07f,.07f,to-from,steel);
        }
        static TextMesh Sign(string text,float x,float y,float z)
        {
            var g=new GameObject(text);g.transform.SetParent(group);g.transform.position=new Vector3(x,y,z);
            var t=g.AddComponent<TextMesh>();t.text=text;t.fontSize=64;t.characterSize=.09f;t.anchor=TextAnchor.MiddleCenter;t.color=Color.white;
            return t;
        }
        static void GroundText(string text,float x,float z)
        { var t=Sign(text,x,.055f,z);t.transform.rotation=Quaternion.Euler(90,0,0); }
        static void CopyNetwork(Scene destination)
        {
            var sourceScene=EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity",OpenSceneMode.Additive);
            try
            {
                var source=sourceScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<NetworkManager>(true)).First();
                var copy=UnityEngine.Object.Instantiate(source.gameObject);copy.name="NetworkManager";
                SceneManager.MoveGameObjectToScene(copy,destination);
                copy.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            }
            finally { EditorSceneManager.CloseScene(sourceScene,true); }
            SceneManager.SetActiveScene(destination);
        }
    }
}
