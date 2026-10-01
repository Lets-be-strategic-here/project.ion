using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ion.Levels;
using Ion.Projection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ArchKit = Ion.Levels.Arch.Arch;

namespace Ion.Tests.PlayMode
{
    /// <summary>
    /// Art bible §11.3 (Lead A's list; PlayMode tests live in Lead C's folder): pattern space on every Sliceable,
    /// architecture on the grid (Arch.Validate on every zone and diorama, before baking) and the per-zone budgets.
    /// </summary>
    public sealed class ArtPlayTests : IonPlayTestBase
    {
        public const int MaxBatchesPerZone = 120;
        public const int MaxTrianglesPerZone = 60000;

        [UnityTest]
        public IEnumerator Art_EverySliceableHasPatternSpace()
        {
            yield return null;
            var problems = new List<string>();
            foreach (RoomContext ctx in Game.Rooms)
            {
                ArchKit.ArtAudit a = ArchKit.Audit(ctx.WorldRoot);
                if (a.Sliceables == 0) problems.Add(ctx.Room.Key + ": no sliceable architecture at all");
                if (a.SliceablesWithoutPattern > 0) problems.Add(ctx.Room.Key + ": " + a);
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [UnityTest]
        public IEnumerator Art_BatchBudgetPerZone()
        {
            yield return null;
            var report = new List<string>();
            var problems = new List<string>();
            foreach (RoomContext ctx in Game.Rooms)
            {
                ArchKit.ArtAudit a = ArchKit.Audit(ctx.WorldRoot);
                report.Add(ctx.Room.Key + ": " + a);
                if (a.Batches > MaxBatchesPerZone)
                {
                    problems.Add(ctx.Room.Key + ": " + a.Batches + " batches > " + MaxBatchesPerZone);
                    report.Add("  " + ctx.Room.Key + " renderers by group: " + Breakdown(ctx.WorldRoot));
                }
                if (a.Triangles > MaxTrianglesPerZone) problems.Add(ctx.Room.Key + ": " + a.Triangles + " triangles > " + MaxTrianglesPerZone);
            }
            Debug.Log("[IonTest] art budget\n" + string.Join("\n", report));
            Assert.IsEmpty(problems, string.Join("\n", problems) + "\n" + string.Join("\n", report));
        }

        /// <summary>Renderer counts grouped by the nearest named ancestor below the zone root (diagnostics).</summary>
        static string Breakdown(Transform root)
        {
            var counts = new Dictionary<string, int>();
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                Transform t = r.transform;
                string key = t.name;
                Transform top = t;
                while (top.parent != null && top.parent != root) top = top.parent;
                if (top != t) key = top.name + "/" + (t.parent == top ? t.name : t.parent.name + "/" + t.name);
                key = System.Text.RegularExpressions.Regex.Replace(key, @"\s*\(\d+\)|\d+", "#");
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
            return string.Join(", ", counts.OrderByDescending(kv => kv.Value).Take(40).Select(kv => kv.Key + " x" + kv.Value));
        }

        /// <summary>
        /// Every zone (and its diorama) is rebuilt unbaked into a scratch root far from the game (fresh Room instance,
        /// same builders) and checked with Arch.Validate: plan min/max on the grid, closed meshes, Visual pieces only
        /// under Interactables.
        /// </summary>
        [UnityTest]
        public IEnumerator Art_ArchitectureOnGrid()
        {
            yield return null;
            var problems = new List<string>();
            var container = new GameObject("ValidateWorld").transform;
            container.position = new Vector3(2000f, 500f, 0f);
            var dioramas = new GameObject("ValidateDioramas").transform;
            dioramas.position = new Vector3(2000f, 700f, 0f);
            Ion.Levels.Arch.ArchStyle before = ArchKit.Style;
            try
            {
                for (int i = 0; i < Game.Rooms.Count; i++)
                {
                    Room fresh = (Room)Activator.CreateInstance(Game.Rooms[i].Room.GetType());
                    var root = new GameObject("Validate " + fresh.Key).transform;
                    root.SetParent(container, false);
                    root.localPosition = new Vector3(i * 100f, 0f, 0f);
                    var ctx = new RoomContext(null, 90 + i, fresh, root, dioramas.position + new Vector3(i * 100f, 0f, 0f));
                    ArchKit.Style = fresh.Style;
                    fresh.Build(root, ctx);
                    var errors = new List<string>();
                    ArchKit.Validate(root, errors);
                    if (ctx.HasDiorama)
                    {
                        ctx.DioramaRoot.SetParent(dioramas, true);
                        ArchKit.Validate(ctx.DioramaRoot, errors);
                    }
                    foreach (string e in errors.Take(12)) problems.Add(fresh.Key + ": " + e);
                    if (errors.Count > 12) problems.Add(fresh.Key + ": ... " + (errors.Count - 12) + " more");
                }
            }
            finally
            {
                ArchKit.Style = before;
                UnityEngine.Object.Destroy(container.gameObject);
                UnityEngine.Object.Destroy(dioramas.gameObject);
            }
            yield return null;
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
