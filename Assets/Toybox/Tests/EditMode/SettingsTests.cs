using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Toybox.Tests
{
    /// <summary>
    /// The player's settings: a static store with defaults, ranges, change events and persistence through
    /// an injectable store, and the two things the platform itself applies - mouse sensitivity (HumanInput)
    /// and field of view (CameraRig).
    /// </summary>
    public class SettingsTests
    {
        MemoryStore store;
        List<Setting> changes;
        Action<Setting> listener;
        Game game;

        [SetUp]
        public void UseAStoreOfOurOwn()
        {
            store = new MemoryStore();
            Settings.Use(store);
            changes = new List<Setting>();
            listener = changes.Add;
            Settings.Changed += listener;
        }

        [TearDown]
        public void GiveTheDefaultStoreBack()
        {
            Settings.Changed -= listener;
            Settings.Use(null);
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
        }

        [Test]
        public void Defaults()
        {
            Assert.AreEqual(QualitySetting.Auto, Settings.Quality);
            Assert.IsNull(Settings.ForcedTier, "Auto leaves the tier to the governor");
            Assert.AreEqual(0.6f, Settings.LensBlur, "the lens blur slider starts at 60%");
            Assert.AreEqual(0.1f, Settings.MouseSensitivity);
            Assert.AreEqual(70f, Settings.FieldOfView, "the art direction assumes 70 degrees");
            Assert.AreEqual(0.8f, Settings.MasterVolume);
            Assert.AreEqual(0.7f, Settings.MusicVolume);
            Assert.AreEqual(1f, Settings.SfxVolume);
            Assert.IsFalse(Settings.ReduceMotion);
            Assert.IsFalse(Settings.HighVisibility);
            Assert.AreSame(store, Settings.Store);
            Assert.AreEqual(0, store.Count, "reading writes nothing");
        }

        [Test]
        public void EverySettingRaisesItsChange_Once_AndNotWhenItIsSetToWhatItAlreadyIs()
        {
            Settings.Quality = QualitySetting.High;
            Settings.LensBlur = 0.25f;
            Settings.MouseSensitivity = 0.2f;
            Settings.FieldOfView = 80f;
            Settings.MasterVolume = 0.5f;
            Settings.MusicVolume = 0.4f;
            Settings.SfxVolume = 0.3f;
            Settings.ReduceMotion = true;
            Settings.HighVisibility = true;
            CollectionAssert.AreEqual(new[]
            {
                Setting.Quality, Setting.LensBlur, Setting.MouseSensitivity, Setting.FieldOfView, Setting.MasterVolume,
                Setting.MusicVolume, Setting.SfxVolume, Setting.ReduceMotion, Setting.HighVisibility,
            }, changes);

            Assert.AreEqual(QualitySetting.High, Settings.Quality);
            Assert.AreEqual(QualityTier.High, Settings.ForcedTier);
            Assert.AreEqual(0.25f, Settings.LensBlur);
            Assert.AreEqual(0.2f, Settings.MouseSensitivity);
            Assert.AreEqual(80f, Settings.FieldOfView);
            Assert.AreEqual(0.5f, Settings.MasterVolume);
            Assert.AreEqual(0.4f, Settings.MusicVolume);
            Assert.AreEqual(0.3f, Settings.SfxVolume);
            Assert.IsTrue(Settings.ReduceMotion);
            Assert.IsTrue(Settings.HighVisibility);

            changes.Clear();
            Settings.Quality = QualitySetting.High;
            Settings.LensBlur = 0.25f;
            Settings.FieldOfView = 80f;
            Settings.ReduceMotion = true;
            CollectionAssert.IsEmpty(changes, "nothing changed, nothing is announced");

            Settings.ResetToDefaults();
            Assert.AreEqual(9, changes.Count, "every setting went back");
            Assert.AreEqual(70f, Settings.FieldOfView);
            Assert.AreEqual(QualitySetting.Auto, Settings.Quality);
        }

        [Test]
        public void ValuesAreClampedToTheirRange()
        {
            Settings.FieldOfView = 5f;
            Assert.AreEqual(Settings.MinFieldOfView, Settings.FieldOfView);
            Settings.FieldOfView = 170f;
            Assert.AreEqual(Settings.MaxFieldOfView, Settings.FieldOfView);
            Settings.MouseSensitivity = 0f;
            Assert.AreEqual(Settings.MinMouseSensitivity, Settings.MouseSensitivity);
            Settings.MouseSensitivity = 50f;
            Assert.AreEqual(Settings.MaxMouseSensitivity, Settings.MouseSensitivity);
            Settings.MasterVolume = -3f;
            Assert.AreEqual(0f, Settings.MasterVolume);
            Settings.LensBlur = 7f;
            Assert.AreEqual(1f, Settings.LensBlur);
            Settings.Quality = (QualitySetting)42;
            Assert.AreEqual(QualitySetting.Auto, Settings.Quality);

            changes.Clear();
            float before = Settings.MusicVolume;
            Settings.MusicVolume = float.NaN;
            Assert.AreEqual(before, Settings.MusicVolume, "nonsense is not a value");
            CollectionAssert.IsEmpty(changes);
        }

        [Test]
        public void ValuesPersistInTheStore()
        {
            Settings.Quality = QualitySetting.Low;
            Settings.FieldOfView = 62f;
            Settings.MouseSensitivity = 0.15f;
            Settings.HighVisibility = true;
            Settings.SfxVolume = 0.25f;
            Settings.Save();
            Assert.AreEqual(1, store.Saves);
            Assert.AreEqual(5, store.Count, "only what was changed is written");

            // Another store: defaults again, and the change is announced.
            changes.Clear();
            var empty = new MemoryStore();
            Settings.Use(empty);
            Assert.AreEqual(QualitySetting.Auto, Settings.Quality);
            Assert.AreEqual(70f, Settings.FieldOfView);
            Assert.IsFalse(Settings.HighVisibility);
            CollectionAssert.AreEquivalent(new[] { Setting.Quality, Setting.FieldOfView, Setting.MouseSensitivity, Setting.HighVisibility, Setting.SfxVolume }, changes);

            // Back to the first one: everything is as it was left - what a new session finds.
            changes.Clear();
            Settings.Use(store);
            Assert.AreEqual(QualitySetting.Low, Settings.Quality);
            Assert.AreEqual(QualityTier.Low, Settings.ForcedTier);
            Assert.AreEqual(62f, Settings.FieldOfView);
            Assert.AreEqual(0.15f, Settings.MouseSensitivity);
            Assert.IsTrue(Settings.HighVisibility);
            Assert.AreEqual(0.25f, Settings.SfxVolume);
            Assert.AreEqual(5, changes.Count);
        }

        [Test]
        public void GarbageInTheStoreFallsBackToDefaultsAndRanges()
        {
            var broken = new MemoryStore();
            broken.SetString("toybox.settings.fieldOfView", "banana");
            broken.SetInt("toybox.settings.quality", 77);
            broken.SetFloat("toybox.settings.mouseSensitivity", 900f);
            broken.SetFloat("toybox.settings.masterVolume", float.NaN);
            Settings.Use(broken);
            Assert.AreEqual(70f, Settings.FieldOfView);
            Assert.AreEqual(QualitySetting.Auto, Settings.Quality);
            Assert.AreEqual(Settings.MaxMouseSensitivity, Settings.MouseSensitivity);
            Assert.AreEqual(Settings.DefaultMasterVolume, Settings.MasterVolume);
        }

        [Test]
        public void AFaultyListenerDoesNotKeepTheOthersFromHearing()
        {
            Action<Setting> faulty = s => throw new InvalidOperationException("listener broke");
            Settings.Changed -= listener;
            Settings.Changed += faulty;
            Settings.Changed += listener;
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("listener broke"));
                Settings.ReduceMotion = true;
                CollectionAssert.AreEqual(new[] { Setting.ReduceMotion }, changes);
                Assert.IsTrue(Settings.ReduceMotion);
            }
            finally
            {
                Settings.Changed -= faulty;
            }
        }

        [Test]
        public void TheMemoryStoreBehavesLikeARealOne()
        {
            var memory = new MemoryStore();
            Assert.IsFalse(memory.Has("a"));
            Assert.AreEqual(7, memory.GetInt("a", 7));
            memory.SetInt("a", 3);
            memory.SetFloat("b", 0.1f);
            memory.SetString("c", "text");
            Assert.IsTrue(memory.Has("a"));
            Assert.AreEqual(3, memory.GetInt("a", 7));
            Assert.AreEqual(0.1f, memory.GetFloat("b", 0f), "floats survive exactly");
            Assert.AreEqual("text", memory.GetString("c", null));
            Assert.AreEqual(5f, memory.GetFloat("c", 5f), "a value of the wrong kind is the fallback");
            memory.Delete("a");
            Assert.IsFalse(memory.Has("a"));
            Assert.AreEqual(2, memory.Count);
            Assert.IsInstanceOf<MemoryStore>(PrefStores.Default, "outside Play Mode nothing touches PlayerPrefs");
            Assert.AreSame(PrefStores.Default, PrefStores.Default);
        }

        // ------------------------------------------------------------------------------------------
        // The two settings the platform applies itself
        // ------------------------------------------------------------------------------------------

        Game Yard()
        {
            game = Game.Create();
            game.LoadLevel(new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            }));
            return game;
        }

        [Test]
        public void HumanInput_TurnsTheViewByTheSensitivitySetting()
        {
            Yard();
            var devices = new FakeDevices();
            var human = new HumanInput(devices);
            devices.PointerLocked = true;
            human.Update(game.Player);   // the frame in which looking starts is skipped

            Assert.AreEqual(Settings.DefaultMouseSensitivity, human.Sensitivity);
            devices.State.Look = new Vector2(100f, 0f);
            human.Update(game.Player);
            Assert.AreEqual(100f * 0.1f, game.Player.Yaw, 1e-3f);

            Settings.MouseSensitivity = 0.25f;
            Assert.AreEqual(0.25f, human.Sensitivity);
            float yaw = game.Player.Yaw;
            devices.State.Look = new Vector2(100f, 0f);
            human.Update(game.Player);
            Assert.AreEqual(100f * 0.25f, Mathf.DeltaAngle(yaw, game.Player.Yaw), 1e-3f, "the same mouse movement, two and a half times the turn");
        }

        [Test]
        public void CameraRig_TakesItsFieldOfViewFromTheSetting()
        {
            Yard();
            CameraRig rig = CameraRig.Create(game);
            try
            {
                Assert.AreEqual(70f, rig.Camera.fieldOfView, 1e-3f);
                Settings.FieldOfView = 85f;
                rig.Apply(1f);
                Assert.AreEqual(85f, rig.Camera.fieldOfView, 1e-3f);
                // Placement is all the rig does: no light of its own, and the scene's lighting is left alone.
                Assert.AreEqual(0, rig.Root.GetComponentsInChildren<Light>().Length);
                Assert.Less(Vector3.Distance(game.Player.Eye, rig.Camera.transform.position), 1e-4f);
                Assert.Less(Quaternion.Angle(game.Player.LookRotation, rig.Camera.transform.rotation), 1e-3f);
            }
            finally
            {
                rig.Dispose();
            }
        }
    }
}
