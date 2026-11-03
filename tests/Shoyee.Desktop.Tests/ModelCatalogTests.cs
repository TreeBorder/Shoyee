using Shoyee.Desktop.Theming;
using Shoyee.Desktop.ViewModels.Settings;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>「模型配置」页：列表、场景绑定与后端格式换算。</summary>
public sealed class ModelCatalogTests
{
    [Fact]
    public void AppliesDefaultCatalogToAllThreeScenes()
    {
        var catalog = new ModelCatalogViewModel();
        catalog.Apply(ModelCatalogSettings.Default);

        var entry = Assert.Single(catalog.Entries);
        Assert.Same(entry, catalog.ChatEntry);
        Assert.Same(entry, catalog.SmallEntry);
        Assert.Same(entry, catalog.VisionEntry);
        Assert.False(catalog.IsChatMissing);
    }

    [Fact]
    public void AddDraftSavesAndKeepsSceneBindings()
    {
        var catalog = new ModelCatalogViewModel();
        catalog.Apply(ModelCatalogSettings.Default);
        var original = catalog.Entries[0];

        catalog.AddCommand.Execute(null);
        var draft = catalog.Editing!;
        Assert.True(catalog.IsDialogOpen);
        Assert.True(draft.IsNew);
        Assert.Equal("添加模型", catalog.DialogTitle);

        draft.Name = "看图模型";
        draft.Model = "vision-model";
        draft.IsVision = true;
        draft.IsChat = false;
        catalog.SaveDialogCommand.Execute(null);

        Assert.False(catalog.IsDialogOpen);
        var saved = catalog.Entries[0];
        Assert.False(saved.IsNew);
        Assert.Same(original, catalog.ChatEntry);
        Assert.Contains(saved, catalog.VisionOptions);
        Assert.DoesNotContain(saved, catalog.ChatOptions);

        var settings = catalog.ToSettings();
        Assert.Equal(2, settings.Entries.Count);
        Assert.Equal("看图模型", settings.Entries[0].Name);
        Assert.Equal([ModelScene.Vision], settings.Entries[0].Scenes);
    }

    [Fact]
    public void UncheckingSceneClearsBindingAndDeleteRemovesEntry()
    {
        var catalog = new ModelCatalogViewModel();
        catalog.Apply(ModelCatalogSettings.Default);
        var entry = catalog.Entries[0];

        entry.IsSmall = false;
        Assert.Null(catalog.SmallEntry);
        Assert.True(catalog.IsSmallMissing);

        catalog.DeleteCommand.Execute(entry);
        Assert.Empty(catalog.Entries);
        Assert.Null(catalog.ChatEntry);
        Assert.Null(catalog.VisionEntry);
    }

    [Fact]
    public void CancelRemovesDraft()
    {
        var catalog = new ModelCatalogViewModel();
        catalog.Apply(ModelCatalogSettings.Default);

        catalog.AddCommand.Execute(null);
        catalog.CancelDialogCommand.Execute(null);

        Assert.False(catalog.IsDialogOpen);
        Assert.Single(catalog.Entries);
    }

    [Fact]
    public void EditSavesThroughDialogAndCancelKeepsOriginal()
    {
        var catalog = new ModelCatalogViewModel();
        catalog.Apply(ModelCatalogSettings.Default);
        var original = catalog.Entries[0];

        catalog.EditCommand.Execute(original);
        Assert.Equal("编辑模型", catalog.DialogTitle);
        catalog.Editing!.Name = "改坏的名字";
        catalog.CancelDialogCommand.Execute(null);
        Assert.Equal("DeepSeek Flash", original.Name);

        catalog.EditCommand.Execute(original);
        catalog.Editing!.Name = "新名字";
        catalog.SaveDialogCommand.Execute(null);
        Assert.Equal("新名字", original.Name);
        Assert.Single(catalog.Entries);
    }
}
