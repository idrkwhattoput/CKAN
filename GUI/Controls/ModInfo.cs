using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

using Autofac;

using CKAN.Versioning;
using CKAN.Extensions;

namespace CKAN.GUI
{
    #if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
    #endif
    public partial class ModInfo : UserControl
    {
        public ModInfo()
        {
            InitializeComponent();
            Contents.OnDownloadClick += gmod => OnDownloadClick?.Invoke(gmod);
        }

        public GUIMod SelectedModule
        {
            set
            {
                var module = value?.ToModule();
                if (value != selectedModule)
                {
                    selectedModule = value;
                    if (module == null)
                    {
                        ModInfoTabControl.Enabled = false;
                    }
                    else
                    {
                        ModInfoTabControl.Enabled = true;
                        UpdateHeaderInfo(value, manager.CurrentInstance.VersionCriteria());
                        LoadTab(value);
                    }
                }
            }
            get => selectedModule;
        }

        public void RefreshModContentsTree()
        {
            Contents.RefreshModContentsTree();
        }

        public event Action<GUIMod>            OnDownloadClick;
        public event Action<SavedSearch, bool> OnChangeFilter;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ModInfoTable.RowStyles[1].Height = TagsHeight;
            if (!string.IsNullOrEmpty(MetadataModuleDescriptionTextBox?.Text))
            {
                MetadataModuleDescriptionTextBox.Height = DescriptionHeight;
            }
        }

        private GUIMod selectedModule;

        private void LoadTab(GUIMod gm)
        {
            switch (ModInfoTabControl.SelectedTab.Name)
            {
                case "MetadataTabPage":
                    Metadata.UpdateModInfo(gm);
                    break;

                case "ContentTabPage":
                    Contents.SelectedModule = gm;
                    break;

                case "RelationshipTabPage":
                    Relationships.SelectedModule = gm;
                    break;

                case "VersionsTabPage":
                    Versions.SelectedModule = gm;
                    if (Platform.IsMono)
                    {
                        // Workaround: make sure the ListView headers are drawn
                        Versions.ForceRedraw();
                    }
                    break;
            }
        }

        // When switching tabs ensure that the resulting tab is updated.
        private void ModInfoTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadTab(SelectedModule);
        }

        private GameInstanceManager manager => Main.Instance.Manager;

        private int TextBoxStringHeight(TextBox tb)
            => tb.Padding.Vertical + tb.Margin.Vertical
                + Util.StringHeight(CreateGraphics(), tb.Text, tb.Font,
                                    tb.Width - tb.Padding.Horizontal - tb.Margin.Horizontal);

        private int DescriptionHeight => TextBoxStringHeight(MetadataModuleDescriptionTextBox);

        private int LinkLabelBottom(LinkLabel lbl)
            => lbl == null ? 0
                           : lbl.Bottom + lbl.Margin.Bottom + lbl.Padding.Bottom;

        private int TagsHeight
            => ModInfoTable.Padding.Vertical + ModInfoTable.Margin.Vertical
                + LinkLabelBottom(MetadataTagsLabelsPanel.Controls.OfType<LinkLabel>().LastOrDefault());

        private void UpdateHeaderInfo(GUIMod gmod, GameVersionCriteria crit)
        {
            var module = gmod.ToModule();
            Util.Invoke(this, () =>
            {
                ModInfoTabControl.SuspendLayout();

                MetadataModuleNameTextBox.Text = module.name;
                UpdateTagsAndLabels(module);
                MetadataModuleAbstractLabel.Text = module.@abstract.Replace("&", "&&");
                MetadataModuleDescriptionTextBox.Text = module.description
                    ?.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
                MetadataModuleDescriptionTextBox.Height = DescriptionHeight;

                // Set/reset alert icons to show a user why a mod is incompatible
                RelationshipTabPage.ImageKey = "";
                VersionsTabPage.ImageKey     = "";
                if (gmod.IsIncompatible)
                {
                    var pageToAlert = module.IsCompatible(crit) ? RelationshipTabPage : VersionsTabPage;
                    pageToAlert.ImageKey = "Stop";
                }

                ModInfoTabControl.ResumeLayout();
            });
        }

        private ModuleLabelList ModuleLabels => Main.Instance.ManageMods.mainModList.ModuleLabels;

        private void UpdateTagsAndLabels(CkanModule mod)
        {
            var registry = RegistryManager.Instance(
                manager.CurrentInstance, ServiceLocator.Container.Resolve<RepositoryDataManager>()
            ).registry;

            Util.Invoke(MetadataTagsLabelsPanel, () =>
            {
                MetadataTagsLabelsPanel.SuspendLayout();
                MetadataTagsLabelsPanel.Controls.Clear();
                var tags = registry?.Tags
                    .Where(t => t.Value.ModuleIdentifiers.Contains(mod.identifier))
                    .OrderBy(t => t.Key)
                    .Select(t => t.Value);
                if (tags != null)
                {
                    foreach (ModuleTag tag in tags)
                    {
                        MetadataTagsLabelsPanel.Controls.Add(TagLabelLink(
                            tag.Name, tag, new LinkLabelLinkClickedEventHandler(TagLinkLabel_LinkClicked)
                        ));
                    }
                }
                var labels = ModuleLabels?.LabelsFor(manager.CurrentInstance.Name)
                    .Where(l => l.ContainsModule(Main.Instance.CurrentInstance.game, mod.identifier))
                    .OrderBy(l => l.Name);
                if (labels != null)
                {
                    foreach (ModuleLabel mlbl in labels)
                    {
                        MetadataTagsLabelsPanel.Controls.Add(TagLabelLink(
                            mlbl.Name, mlbl, new LinkLabelLinkClickedEventHandler(LabelLinkLabel_LinkClicked)
                        ));
                    }
                }
                MetadataTagsLabelsPanel.ResumeLayout();
                ModInfoTable.RowStyles[1].Height = TagsHeight;
            });
        }

        private LinkLabel TagLabelLink(string name, object tag, LinkLabelLinkClickedEventHandler onClick)
        {
            var link = new LinkLabel()
            {
                AutoSize     = true,
                LinkColor    = SystemColors.GrayText,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin       = new Padding(0, 2, 4, 2),
                Text         = name,
                Tag          = tag,
            };
            link.LinkClicked += onClick;
            ToolTip.SetToolTip(link, Properties.Resources.FilterLinkToolTip);
            return link;
        }

        private void TagLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var link = sender as LinkLabel;
            var merge = ModifierKeys.HasAnyFlag(Keys.Control, Keys.Shift);
            OnChangeFilter?.Invoke(
                ModList.FilterToSavedSearch(GUIModFilter.Tag, link.Tag as ModuleTag, null),
                merge);
        }

        private void LabelLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var link = sender as LinkLabel;
            var merge = ModifierKeys.HasAnyFlag(Keys.Control, Keys.Shift);
            OnChangeFilter?.Invoke(
                ModList.FilterToSavedSearch(GUIModFilter.CustomLabel, null, link.Tag as ModuleLabel),
                merge);
        }

        private void Metadata_OnChangeFilter(SavedSearch search, bool merge)
        {
            OnChangeFilter?.Invoke(search, merge);
        }
    }
}
