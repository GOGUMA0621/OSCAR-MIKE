using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Netcode;
using OskarMike.Network.Player;

namespace OskarMike.EditorTools
{
    [InitializeOnLoad]
    public static class RangeCourseValidation
    {
        const string Pending="RangeCourse.Validation";
        static double started;
        static RangeCourseValidation()
        {
            if(SessionState.GetBool(Pending,false)) { started=EditorApplication.timeSinceStartup; EditorApplication.update+=Poll; }
        }
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RangeCourse.unity");
            Physics.SyncTransforms();
            Require(Physics.Raycast(new Vector3(22,.4f,9),Vector3.forward,23)==false,"Crawl route blocked");
            Require(Physics.Raycast(new Vector3(22,1.2f,7),Vector3.forward,2)==false,"Unexpected crawl front obstruction");
            Require(Physics.Raycast(new Vector3(22,0.1f,20),Vector3.up,out var roof,2)&&Mathf.Abs(roof.point.y-.8f)<.01f,"Crawl roof clearance");
            Require(Physics.Raycast(new Vector3(40,.5f,9),Vector3.forward,43)==false,"Return route blocked");
            Require(Physics.Raycast(new Vector3(32,.5f,54),Vector3.right,8)==false,"U turn blocked");
            Require(Physics.Raycast(new Vector3(6,.5f,9),Vector3.forward,48)==false,"Baseline route blocked");
            var output=Environment.GetEnvironmentVariable("RANGE_OUTPUT");
            if(!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                Capture(output+"/range-overview.png",new Vector3(135,240,-105),new Vector3(0,0,143),true,175);
                Capture(output+"/obstacle-course.png",new Vector3(76,55,-30),new Vector3(21,0,28),false,0);
            }
            Debug.Log("RANGE_GEOMETRY_CHECKS_OK");
            SessionState.SetBool(Pending,true); started=EditorApplication.timeSinceStartup;
            EditorApplication.update-=Poll;EditorApplication.update+=Poll;
            EditorApplication.isPlaying=true;
        }
        static void Capture(string path,Vector3 position,Vector3 look,bool ortho,float size)
        {
            var go=new GameObject("Temporary preview");var c=go.AddComponent<Camera>();
            c.transform.position=position;c.transform.LookAt(look);c.farClipPlane=1000;c.orthographic=ortho;c.orthographicSize=size;
            c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.12f,.15f,.18f);
            var rt=new RenderTexture(1600,1200,24);c.targetTexture=rt;c.Render();
            var previous=RenderTexture.active;RenderTexture.active=rt;
            var image=new Texture2D(1600,1200,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1600,1200),0,0);image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());RenderTexture.active=previous;
            c.targetTexture=null;UnityEngine.Object.DestroyImmediate(image);rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);
        }
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup-started<18)return;
            EditorApplication.update-=Poll;SessionState.SetBool(Pending,false);
            try
            {
                var nm=NetworkManager.Singleton;Require(nm!=null&&nm.IsHost,"Host did not start");
                var player=nm.LocalClient.PlayerObject;Require(player!=null,"Player did not spawn");
                var controller=player.GetComponent<PlayerNetworkController>();Require(controller!=null,"Controller missing");
                var field=typeof(PlayerNetworkController).GetField("gameplayInputEnabled",BindingFlags.NonPublic|BindingFlags.Instance);
                Require(field!=null&&(bool)field.GetValue(controller),"Gameplay input disabled");
                Require(player.transform.position.y>-.2f&&player.transform.position.y<2,"Player not on apron");
                Debug.Log("RANGE_PLAYMODE_STARTUP_OK player="+player.transform.position);
                EditorApplication.Exit(0);
            }
            catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
        static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    }
}
