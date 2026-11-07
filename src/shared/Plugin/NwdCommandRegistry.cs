using System;
using System.Collections.Generic;
using Bimwright.Nwd.Shared.Infrastructure;
using Bimwright.Nwd.Shared.Handlers;

namespace Bimwright.Nwd.Shared.Plugin;

public static partial class NwdCommandRegistry
{
    public static IReadOnlyDictionary<string, INwdCommand> Build(PluginOptions options)
    {
        var dict = new Dictionary<string, INwdCommand>(StringComparer.OrdinalIgnoreCase);
        void Add(INwdCommand cmd) => dict.Add(cmd.Name, cmd);

        Add(new HealthCheckHandler(options.NavisworksYear));
        Add(new ListRecentFilesHandler());
        Add(new OpenFileHandler());
        Add(new ImportModelHandler());
        Add(new GetDocumentInfoHandler());
        Add(new GetModelStatisticsHandler());
        Add(new GetModelTreeHandler());
        Add(new GetItemPropertiesHandler());
        Add(new BatchGetPropertiesHandler());
        Add(new FindItemsHandler());
        Add(new FindItemsByNameHandler());
        Add(new GetCurrentSelectionHandler());
        Add(new ClearSelectionHandler());
        Add(new SelectItemsBySearchHandler());
        Add(new ListSetsHandler());
        Add(new GetSelectionSetItemsHandler());
        Add(new ExecuteSearchSetHandler());
        Add(new ListViewpointsHandler());
        Add(new GetCurrentViewpointHandler());
        Add(new GotoViewpointHandler());
        Add(new SaveViewpointHandler());
        Add(new HideItemsHandler());
        Add(new UnhideAllHandler());
        Add(new ResolveIdsHandler());
        Add(new ListModelsHandler());
        Add(new GetDocumentMetadataHandler());
        Add(new PeekFileInfoHandler());
        Add(new GetSceneBoundsHandler());
        Add(new GetItemHierarchyHandler());
        Add(new GetItemsGeometryHandler());
        Add(new GetVisibilityStateHandler());
        Add(new DescribePropertiesHandler());
        Add(new PropertyValueHistogramHandler());
        Add(new ExportPropertiesTableHandler());
        Add(new GetHyperlinksHandler());
        Add(new GetGridsHandler());
        Add(new LocateItemsOnGridHandler());
        Add(new GetCommentsHandler());
        Add(new ListSheetsHandler());
        Add(new ListAlignmentsHandler());
        Add(new CaptureImageHandler());
        Add(new GetSectionHandler());
        Add(new GetRedlinesHandler());
        Add(new SelectItemsHandler());
        Add(new IsolateItemsHandler());
        Add(new SetItemFlagsHandler());
        Add(new ResetVisibilityHandler());
        Add(new OverrideAppearanceHandler());
        Add(new ColorByPropertyHandler());
        Add(new ResetAppearanceHandler());
        Add(new ZoomToItemsHandler());
        Add(new SetCameraHandler());
        Add(new SetSectionHandler());
        Add(new SetBackgroundHandler());
        Add(new SetRedlinesHandler());
        Add(new ManageSavedViewpointHandler());
        Add(new AddCommentHandler());
        Add(new SaveViewpointWithOverridesHandler());
        Add(new BatchViewpointImagesHandler());
        Add(new ListTimelinerTasksHandler());
        Add(new GetTimelinerTaskHandler());
        Add(new GetTimelinerConfigHandler());
        Add(new TimelinerCoverageHandler());
        Add(new FindTasksForItemsHandler());
        Add(new ExportTimelinerScheduleHandler());
        Add(new CreateTimelinerTaskHandler());
        Add(new UpdateTimelinerTaskHandler());
        Add(new DeleteTimelinerTaskHandler());
        Add(new MoveTimelinerTaskHandler());
        Add(new AttachItemsToTaskHandler());
        Add(new AutoAttachTasksByPropertyHandler());
        Add(new SyncTimelinerDataSourceHandler());
        Add(new RecalculateTaskSummariesHandler());
        Add(new SortTimelinerTasksHandler());
        Add(new ManageSimulationAppearanceHandler());
        Add(new SendCodeHandler());
        Add(new RunBakedToolHandler());
        Add(new ApplyBakeHandler());

        return dict;
    }
}
