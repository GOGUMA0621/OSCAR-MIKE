#if UNITY_EDITOR_WIN
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace OskarMike.Items.Editor
{
    public static class LootCatalogExcelBridge
    {
        private const string WorkbookPath = "Tools/LootCatalog/LootCatalog.xlsx";
        private const string CatalogPath = "Tools/LootCatalog/LootCatalog.tsv";

        [MenuItem("Tools/OSCAR-MIKE/Items/Sync Excel to TSV and Import")]
        public static void SyncAndImport()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
            {
                Debug.LogError("[LootCatalog] Unity 프로젝트 루트를 확인할 수 없습니다.");
                return;
            }

            string workbook = Path.Combine(projectRoot, WorkbookPath);
            string catalog = Path.Combine(projectRoot, CatalogPath);
            string bridge = Path.Combine(projectRoot, "Tools/LootCatalog/SyncLootCatalog.ps1");
            if (!File.Exists(workbook) || !File.Exists(bridge))
            {
                Debug.LogError($"[LootCatalog] Excel 원본 또는 브릿지 파일이 없습니다.\n{workbook}\n{bridge}");
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{bridge}\" -Workbook \"{workbook}\" -Output \"{catalog}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using Process process = Process.Start(startInfo);
            if (process == null)
            {
                Debug.LogError("[LootCatalog] Excel 동기화 프로세스를 시작하지 못했습니다.");
                return;
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                Debug.LogError($"[LootCatalog] Excel → TSV 동기화 실패\n{error}");
                return;
            }

            Debug.Log($"[LootCatalog] {output.Trim()}");
            LootCatalogImporter.ImportCatalog();
        }
    }
}
#endif
