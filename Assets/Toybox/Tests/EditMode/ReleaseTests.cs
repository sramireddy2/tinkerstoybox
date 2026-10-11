using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Toybox.EditorTools;
using Toybox.Engine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toybox.Tests
{
    /// <summary>
    /// What a stranger gets: the README, the documents under docs/, the page the build is published with
    /// and the notices that have to travel with it. Each test is a defect the final review found there -
    /// a controls table in other words than the game's, links into folders that are not in the
    /// repository, fonts shipped without their licence, a leftover asset the setup kept recreating.
    ///
    /// The documents are beside Assets/, not in it: a copy of the project made for a build (Assets,
    /// Packages, ProjectSettings, tools) does not have them, and there these tests have nothing to read.
    /// </summary>
    public class ReleaseTests
    {
        static string Root => Path.GetDirectoryName(Application.dataPath);
        static string TemplateDir => Path.Combine(Application.dataPath, "WebGLTemplates/Toybox");

        static string Read(string relative)
        {
            string path = Path.Combine(Root, relative);
            if (!File.Exists(path))
            {
                // Only a build copy is without its documents; in the repository a missing one is a defect.
                if (!Directory.Exists(Path.Combine(Root, "docs"))) Assert.Ignore("this copy of the project has no documents (" + relative + ")");
                Assert.Fail(relative + " is missing");
            }
            return File.ReadAllText(path);
        }

        static List<string> Documents()
        {
            var files = new List<string> { "README.md" };
            string docs = Path.Combine(Root, "docs");
            if (!Directory.Exists(docs)) Assert.Ignore("this copy of the project has no documents");
            foreach (string path in Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories))
                files.Add(path.Substring(Root.Length + 1).Replace('\\', '/'));
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        // ---- The README says what the game does ------------------------------------------------------------

        [Test]
        public void TheReadme_ListsEveryCampaignLevelByItsNumberAndTitle()
        {
            string readme = Read("README.md");
            int campaign = 0;
            foreach (LevelEntry level in LevelRegistry.All)
            {
                if (level.Phase <= 0) continue;
                campaign++;
                StringAssert.Contains(level.Id + " " + level.Title, readme, "the README's list of levels");
            }
            Assert.AreEqual(15, campaign, "the campaign the README calls \"the fifteen levels\"");
            // The address takes a level's name as well as its number: the README's example has to be a real one.
            Match example = Regex.Match(readme, @"\?level=([a-z][a-z0-9-]*)");
            Assert.IsTrue(example.Success, "the README shows a level address by name");
            Assert.IsNotNull(LevelRegistry.Find(example.Groups[1].Value), "?level=" + example.Groups[1].Value + " is no level");
        }

        [Test]
        public void TheReadmesControls_AreInTheGamesOwnWords()
        {
            string readme = Read("README.md");
            // The HUD's pills and every line of the levels say "pick up", "let go", "turn" and "flip".
            StringAssert.Contains("Pick up / let go", readme);
            StringAssert.Contains("Turn the held toy", readme);
            StringAssert.Contains("Flip the held toy", readme);
            StringAssert.DoesNotContain("Grab / release", readme);
            StringAssert.DoesNotContain("Tip the held", readme);
            // With the game's menus a click beside the pause card does nothing: Esc or the Resume button goes on.
            StringAssert.DoesNotContain("a click resumes", readme);
            StringAssert.Contains("**Resume**", readme);
            // While playing R is a hold, and the level keys and the bot's key are debug keys behind &debug=1
            // (the README is the short page; the developer's guide has the address parameters).
            StringAssert.DoesNotContain("(at once)", readme);
            StringAssert.Contains("hold it for half a second", readme);
            StringAssert.Contains("`&debug=1` turns the debug keys on", Read("docs/DEVELOPMENT.md"));
            // The quarter-turn and the yaw step are the engine's.
            Assert.AreEqual(15f, PerspectiveGrabber.YawStepDegrees);
            Assert.AreEqual(90f, PerspectiveGrabber.PitchStepDegrees);
            StringAssert.Contains("15° steps", readme);
            StringAssert.Contains("quarter turn", readme);
        }

        [Test]
        public void TheReadme_NamesTheEditorTheProjectIsMadeWith_AndEveryTool()
        {
            // The README is the short page a visitor reads; the tools and the release are in the developer's
            // guide it links to.
            string readme = Read("README.md");
            string guide = Read("docs/DEVELOPMENT.md");
            string architecture = Read("docs/ARCHITECTURE.md");
            StringAssert.Contains(Application.unityVersion, readme, "the Unity version to install");
            StringAssert.Contains(Application.unityVersion, guide);
            StringAssert.Contains(Application.unityVersion, architecture);
            StringAssert.Contains("](docs/DEVELOPMENT.md)", readme, "the README sends developers to the guide");

            string tools = Path.Combine(Root, "tools");
            int found = 0;
            foreach (string path in Directory.GetFiles(tools))
            {
                string name = Path.GetFileName(path);
                if (!name.EndsWith(".ps1") && !name.EndsWith(".py")) continue;
                found++;
                StringAssert.Contains(name, guide, "docs/DEVELOPMENT.md says what tools/" + name + " is for");
                StringAssert.Contains(name, architecture, "ARCHITECTURE names tools/" + name);
            }
            Assert.GreaterOrEqual(found, 6, "unity, typecheck, build-clone, deploy, editor-check, webgl_check");

            // The release is a procedure, and it is written down: the fast build is not the one to publish.
            StringAssert.Contains("## Releasing", guide);
            StringAssert.Contains("## Releasing", architecture);
            StringAssert.Contains("build-clone.ps1 -Committed -Opt RuntimeSpeed", guide);
            StringAssert.Contains("deploy.ps1 -SkipBuild", guide);
        }

        // ---- The documents point at what a clone has -------------------------------------------------------

        [Test]
        public void EveryLinkInTheDocuments_LeadsToAFileInTheRepository()
        {
            var link = new Regex(@"\]\(([^)\s]+)\)");
            // Code is not prose: a regular expression in a code span ("[0-5](Hint|Ui)") is no link.
            var code = new Regex(@"```.*?```|`[^`\n]*`", RegexOptions.Singleline);
            int links = 0;
            foreach (string document in Documents())
            {
                string text = code.Replace(Read(document), " ");
                string folder = Path.GetDirectoryName(Path.Combine(Root, document));
                foreach (Match match in link.Matches(text))
                {
                    string target = match.Groups[1].Value;
                    if (target.StartsWith("http://") || target.StartsWith("https://") || target.StartsWith("mailto:")) continue;
                    int hash = target.IndexOf('#');
                    if (hash == 0) continue;   // a heading of the same document
                    if (hash > 0) target = target.Substring(0, hash);
                    links++;
                    string full = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(target)));
                    Assert.IsTrue(File.Exists(full) || Directory.Exists(full), document + " links to " + match.Groups[1].Value + ", which does not exist");
                    StringAssert.DoesNotContain("/tools/out/", full.Replace('\\', '/') + "/", document + " links into tools/out, which is not in the repository");
                }
            }
            Assert.Greater(links, 10, "the README's pictures and documents, the notes' cross references");
        }

        [Test]
        public void NoDocument_NamesAPathOnSomebodysMachine()
        {
            var local = new Regex(@"[A-Za-z]:[\\/]+Users[\\/]|OneDrive[\\/]+Desktop", RegexOptions.IgnoreCase);
            foreach (string document in Documents())
            {
                Match match = local.Match(Read(document));
                Assert.IsFalse(match.Success, document + " has a local path: " + (match.Success ? match.Value : ""));
            }
        }

        [Test]
        public void WhereADocumentSendsTheReaderToToolsOut_ItSaysThatFolderIsNotInTheRepository()
        {
            Documents();   // a copy of the project without its documents has nothing to check here
            StringAssert.Contains("/tools/out/", Read(".gitignore").Replace("\r", ""), "tools/out is git-ignored");
            foreach (string document in new[] { "docs/ARCHITECTURE.md", "docs/LEVELS.md", "docs/LEVEL_AUTHORING.md", "docs/design-notes/README.md" })
            {
                string text = Regex.Replace(Read(document), @"\s+", " ");
                if (!text.Contains("tools/out")) continue;
                StringAssert.IsMatch(@"tools/out`?[^.]{0,160}not in the repository", text, document + " names tools/out without saying it is local");
            }
            // What a reader needs from there is in docs/.
            foreach (string kept in new[]
            {
                "docs/LEVEL_AUTHORING.md", "docs/design-notes/README.md", "docs/design-notes/phase-1-campaign.md", "docs/design-notes/phase-2-campaign.md",
                "docs/design-notes/phase-3-campaign.md", "docs/media/title.png", "docs/media/campaign-spawn-sheet.png",
            })
                Assert.IsTrue(File.Exists(Path.Combine(Root, kept)), kept);
        }

        [Test]
        public void TheDesignDocuments_SayThatTheGameIsBuilt()
        {
            string art = Read("docs/ART_BIBLE.md");
            StringAssert.DoesNotContain("Nothing in this document has been built", art);
            StringAssert.Contains("**Status.** Built.", art);
            StringAssert.Contains("Where the game departs from the art bible", art, "it points to the list of departures in ARCHITECTURE");
            StringAssert.Contains("## Where the game departs from the art bible", Read("docs/ARCHITECTURE.md"));

            string architecture = Read("docs/ARCHITECTURE.md");
            StringAssert.DoesNotContain("Not covered by anything: the WebGL build", architecture);
            StringAssert.DoesNotContain("Nobody has listened", architecture);
            StringAssert.DoesNotContain("The campaign so far", architecture);
            StringAssert.Contains("Never end a loop on a float", architecture, "the lesson of the build that froze (Level 5's dashes)");

            // The three sections of LEVELS that were written before the build say so under their headings.
            string levels = Read("docs/LEVELS.md").Replace("\r", "");
            foreach (string heading in new[] { "### 0.4 Engine requests", "## Appendix B", "## Appendix C" })
            {
                int at = levels.IndexOf("\n" + heading, StringComparison.Ordinal);
                Assert.GreaterOrEqual(at, 0, heading);
                int line = levels.IndexOf('\n', at + 1);
                string after = levels.Substring(line, 200).TrimStart('\n');
                StringAssert.StartsWith("> **", after, heading + ": the first thing under it is the note that it predates the build");
            }
        }

        // ---- The fonts' notices travel with the build ------------------------------------------------------

        [Test]
        public void EveryFontFileInTheProject_HasItsNoticeAndLicenceInThePublishedPage()
        {
            string noticesPath = Path.Combine(TemplateDir, "licenses.txt");
            Assert.IsTrue(File.Exists(noticesPath), "Assets/WebGLTemplates/Toybox/licenses.txt: Unity copies the template's files into every build");
            string notices = Flat(File.ReadAllText(noticesPath));
            StringAssert.Contains("SIL OPEN FONT LICENSE Version 1.1", notices);
            StringAssert.Contains("PERMISSION & CONDITIONS", notices, "the licence text itself, not a link to it");
            StringAssert.Contains("provided that each copy contains the above copyright notice and this license", notices);

            // Every font file under Assets ships or may ship (TextMesh Pro's fallback is a dynamic font asset
            // under Resources, which keeps its .ttf in the build). Each has a licence file beside it, and what
            // that file says before the licence text - the copyright lines - must be in the page's notices.
            var fonts = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Font", new[] { "Assets" })) fonts.Add(AssetDatabase.GUIDToAssetPath(guid));
            Assert.AreEqual(3, fonts.Count, "Unbounded, Figtree and TextMesh Pro's Liberation Sans - a new font needs its notice in licenses.txt, the README and ART_BIBLE 10.1: " + string.Join(", ", fonts));

            string readme = Flat(Read("README.md"));
            int holders = 0;
            var folders = new HashSet<string>();
            foreach (string font in fonts) folders.Add(Path.GetDirectoryName(font));
            foreach (string folder in folders)
            {
                string[] licences = Directory.GetFiles(Path.Combine(Root, folder), "*OFL*.txt");
                Assert.Greater(licences.Length, 0, folder + ": a font without its licence beside it");
                foreach (string licence in licences)
                {
                    foreach (string raw in File.ReadAllLines(licence))
                    {
                        string line = Flat(raw);
                        if (line.StartsWith("This Font Software is licensed")) break;
                        if (line.IndexOf("copyright", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        holders++;
                        StringAssert.Contains(line, notices, "licenses.txt lacks the notice of " + Path.GetFileName(licence));
                        StringAssert.Contains(line, readme, "the README's credits lack the notice of " + Path.GetFileName(licence));
                    }
                }
            }
            Assert.GreaterOrEqual(holders, 4, "Unbounded, Figtree, and Liberation Sans' two (Google, Red Hat)");

            // And the page says where they are.
            StringAssert.Contains("licenses.txt", File.ReadAllText(Path.Combine(TemplateDir, "index.html")));
        }

        static string Flat(string text) => Regex.Replace(text, @"\s+", " ").Trim();

        // ---- Housekeeping ------------------------------------------------------------------------------

        [Test]
        public void TheVersion_IsARelease()
        {
            Assert.IsTrue(Version.TryParse(PlayerSettings.bundleVersion, out Version version), PlayerSettings.bundleVersion);
            Assert.GreaterOrEqual(version, new Version(1, 0, 0), "the campaign is complete: not a 0.x any more (CoreSetup.ConfigurePlayer)");
        }

        [Test]
        public void TheStartingPipelineAsset_IsGone_AndTheSetupDoesNotBringItBack()
        {
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(CoreSetup.UrpAssetPath),
                CoreSetup.UrpAssetPath + ": nothing refers to it once the tier assets are assigned");

            RenderPipelineAsset before = GraphicsSettings.defaultRenderPipeline;
            Assert.IsNotNull(before, "a tier asset is the default pipeline");
            List<SetupStep> steps = ProjectSetup.Filter(ProjectSetup.Discover(), "CoreSetup.ConfigureRenderPipeline, CoreSetup.ConfigureShadows, PipelineSetup.ConfigureQualityLevels");
            Assert.AreEqual(3, steps.Count);
            Assert.AreEqual(0, ProjectSetup.RunSteps(steps));

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(CoreSetup.UrpAssetPath), "the setup made it again");
            Assert.IsFalse(File.Exists(Path.Combine(Root, CoreSetup.UrpAssetPath)));
            Assert.AreSame(before, GraphicsSettings.defaultRenderPipeline);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UniversalRendererData>(CoreSetup.RendererPath), "the shared renderer is the core's to make, and stays");
        }
    }
}
