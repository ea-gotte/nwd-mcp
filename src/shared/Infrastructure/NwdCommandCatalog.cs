using System;
using System.Collections.Generic;

namespace Bimwright.Nwd.Shared.Infrastructure;

public readonly record struct NwdCommandInfo(string Name, bool IsReadOnly);

public static class NwdCommandCatalog
{
    public static readonly IReadOnlyList<NwdCommandInfo> All = new[]
    {
        new NwdCommandInfo("health_check", true),
        new NwdCommandInfo("list_recent_files", true),
        new NwdCommandInfo("open_file", false),
        new NwdCommandInfo("import_model", false),
        new NwdCommandInfo("get_document_info", true),
        new NwdCommandInfo("get_model_statistics", true),
        new NwdCommandInfo("get_model_tree", true),
        new NwdCommandInfo("get_item_properties", true),
        new NwdCommandInfo("batch_get_properties", true),
        new NwdCommandInfo("find_items", true),
        new NwdCommandInfo("find_items_by_name", true),
        new NwdCommandInfo("get_current_selection", true),
        new NwdCommandInfo("clear_selection", false),
        new NwdCommandInfo("select_items_by_search", false),
        new NwdCommandInfo("list_sets", true),
        new NwdCommandInfo("get_selection_set_items", true),
        new NwdCommandInfo("execute_search_set", true),   // read; select handled in wrapper/handler
        new NwdCommandInfo("list_viewpoints", true),
        new NwdCommandInfo("get_current_viewpoint", true),
        new NwdCommandInfo("goto_viewpoint", false),
        new NwdCommandInfo("save_viewpoint", false),
        new NwdCommandInfo("hide_items", false),
        new NwdCommandInfo("unhide_all", false),
        new NwdCommandInfo("resolve_ids", true),
        new NwdCommandInfo("list_models", true),
        new NwdCommandInfo("get_document_metadata", true),
        new NwdCommandInfo("peek_file_info", true),
        new NwdCommandInfo("get_scene_bounds", true),
        new NwdCommandInfo("get_item_hierarchy", true),
        new NwdCommandInfo("get_items_geometry", true),
        new NwdCommandInfo("get_visibility_state", true),
        new NwdCommandInfo("describe_properties", true),
        new NwdCommandInfo("property_value_histogram", true),
        new NwdCommandInfo("export_properties_table", true),
        new NwdCommandInfo("get_hyperlinks", true),
        new NwdCommandInfo("get_grids", true),
        new NwdCommandInfo("locate_items_on_grid", true),
        new NwdCommandInfo("get_comments", true),
        new NwdCommandInfo("list_sheets", true),
        new NwdCommandInfo("list_alignments", true),
        new NwdCommandInfo("capture_image", true),
        new NwdCommandInfo("get_section", true),
        new NwdCommandInfo("get_redlines", true),
        new NwdCommandInfo("select_items", false),
        new NwdCommandInfo("isolate_items", false),
        new NwdCommandInfo("set_item_flags", false),
        new NwdCommandInfo("reset_visibility", false),
        new NwdCommandInfo("override_appearance", false),
        new NwdCommandInfo("color_by_property", false),
        new NwdCommandInfo("reset_appearance", false),
        new NwdCommandInfo("zoom_to_items", false),
        new NwdCommandInfo("set_camera", false),
        new NwdCommandInfo("set_section", false),
        new NwdCommandInfo("set_background", false),
        new NwdCommandInfo("set_redlines", false),
        new NwdCommandInfo("manage_saved_viewpoint", false),
        new NwdCommandInfo("add_comment", false),
        new NwdCommandInfo("save_viewpoint_with_overrides", false),
        new NwdCommandInfo("batch_viewpoint_images", false),
        new NwdCommandInfo("send_code", false),
        new NwdCommandInfo("run_baked_tool", false),
        new NwdCommandInfo("apply_bake", false),
    };

    public static bool TryGet(string name, out NwdCommandInfo info)
    {
        foreach (var c in All) if (c.Name == name) { info = c; return true; }
        info = default; return false;
    }
}
