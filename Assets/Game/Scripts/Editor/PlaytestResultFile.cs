using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Итог автопрогона в один файл <c>Logs/&lt;имя&gt;</c> в папке проекта (перезаписывается при каждом прогоне),
    /// чтобы не читать лог Unity. Первая строка — <c>PASS|FAIL|RUNNING|ABORTED N/M — имя, время</c>,
    /// дальше только упавшие проверки: название, ожидалось, факт.
    /// </summary>
    internal sealed class PlaytestResultFile
    {
        private readonly string title;
        private readonly List<string> failed = new List<string>();
        private int total;

        public PlaytestResultFile(string title, string fileName)
        {
            this.title = title;
            Path = System.IO.Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Logs", fileName);
        }

        public string Path { get; }
        public int Total => total;
        public int Failures => failed.Count;

        /// <summary>Сбрасывает счётчики и пишет RUNNING, чтобы старый итог не приняли за новый.</summary>
        public void Begin()
        {
            total = 0;
            failed.Clear();
            Write($"RUNNING — {title}, {Stamp()}\n");
        }

        /// <summary>
        /// Учитывает проверку. Факт берётся из <paramref name="what"/> после последнего «: »
        /// (так устроены сообщения проверок), иначе — «условие не выполнено».
        /// </summary>
        public void Add(bool ok, string what, string expected)
        {
            total++;
            if (ok)
                return;
            var name = what;
            var actual = "condition is false";
            var cut = what.LastIndexOf(": ", StringComparison.Ordinal);
            if (cut > 0)
            {
                name = what.Substring(0, cut);
                actual = what.Substring(cut + 2);
            }

            failed.Add($"FAIL {name}\n  expected: {expected ?? "condition is true"}\n  actual:   {actual}");
        }

        /// <summary>Пишет итог: PASS/FAIL N/M и упавшие проверки.</summary>
        public void Finish() => Write(Summary(failed.Count == 0 ? "PASS" : "FAIL"));

        /// <summary>Прогон оборван (вышли из Play Mode): итог ABORTED с причиной и тем, что успело упасть.</summary>
        public void Abort(string reason) => Write(Summary("ABORTED") + $"ABORTED {reason}\n");

        private string Summary(string status)
        {
            var sb = new StringBuilder();
            sb.Append($"{status} {total - failed.Count}/{total} — {title}, {Stamp()}\n");
            foreach (var line in failed)
                sb.Append(line).Append('\n');
            return sb.ToString();
        }

        private void Write(string text)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Playtest: cannot write {Path}: {e.Message}");
            }
        }

        private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
