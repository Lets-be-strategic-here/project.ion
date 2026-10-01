using Ion.Levels.Arch;
using Ion.Presentation;
using NUnit.Framework;
using UnityEngine;
using ArchKit = Ion.Levels.Arch.Arch;

namespace Ion.Tests
{
    /// <summary>The Ultra graphics tier: preference range, names, Ultra-only detail materials and the Arch scope.</summary>
    public class UltraTierTests
    {
        int _savedPref;

        [SetUp]
        public void Save() => _savedPref = PlayerPrefs.GetInt(QualityTier.PrefKey, QualityTier.Auto);

        [TearDown]
        public void RestorePref() => QualityTier.Set(_savedPref);

        [Test]
        public void QualityTier_UltraIsSelectableAboveHigh()
        {
            QualityTier.Set(QualityTier.Ultra);
            Assert.AreEqual(QualityTier.Ultra, QualityTier.Current);
            Assert.IsTrue(QualityTier.IsUltra);
            Assert.AreEqual("Ultra", QualityTier.Name(QualityTier.Ultra));
            QualityTier.Set(99);
            Assert.AreEqual(QualityTier.Ultra, QualityTier.Preference, "out-of-range preferences clamp to Ultra");
            QualityTier.Set(QualityTier.High);
            Assert.IsFalse(QualityTier.IsUltra);
            Assert.AreEqual(new[] { "Auto", "Low", "Med", "High", "Ultra" },
                new[] { QualityTier.Name(-1), QualityTier.Name(0), QualityTier.Name(1), QualityTier.Name(2), QualityTier.Name(3) });
        }

        [Test]
        public void Palette_UltraTwinCollapsesOffUltraAndKeepsItsRole()
        {
            Material normal = Palette.Get(Mat.Limestone);
            Material ultra = Palette.GetUltra(Mat.Limestone);
            Assert.AreNotSame(normal, ultra);
            Assert.AreSame(ultra, Palette.GetUltra(Mat.Limestone), "cached");
            Assert.IsTrue(Palette.IsUltra(ultra));
            Assert.IsFalse(Palette.IsUltra(normal));
            Assert.AreEqual(1f, ultra.GetFloat("_UltraOnly"));
            Assert.AreEqual(0f, normal.GetFloat("_UltraOnly"));
            Assert.IsTrue(Palette.TryGetMat(ultra, out Mat role));
            Assert.AreEqual(Mat.Limestone, role);
        }

        [Test]
        public void Arch_UltraScopeMakesSoftUltraOnlyPieces()
        {
            var root = new GameObject("UltraScopeTest").transform;
            try
            {
                GameObject plain = ArchKit.Box(root, Vector3.zero, Vector3.one, Mat.Paper);
                GameObject detail;
                using (ArchKit.Ultra())
                {
                    Assert.IsTrue(ArchKit.UltraOnly);
                    detail = ArchKit.Box(root, Vector3.zero, Vector3.one, Mat.Paper);
                }
                Assert.IsFalse(ArchKit.UltraOnly, "the scope closes");
                Assert.IsFalse(Palette.IsUltra(plain.GetComponent<MeshRenderer>().sharedMaterial));
                Assert.IsTrue(Palette.IsUltra(detail.GetComponent<MeshRenderer>().sharedMaterial));
                Assert.IsNotNull(plain.GetComponent<Collider>(), "a Solid box has a collider");
                Assert.IsNull(detail.GetComponent<Collider>(), "Ultra detail never changes collision");
                Assert.IsNotNull(detail.GetComponent<Ion.Projection.Sliceable>(), "Ultra detail stays sliceable");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }
    }
}
