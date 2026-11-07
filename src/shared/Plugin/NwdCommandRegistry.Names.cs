using System;

namespace Bimwright.Nwd.Shared.Plugin;

public static partial class NwdCommandRegistry
{
    public static readonly string[] RegisteredNames = new[]
    {
        "health_check",
        "list_recent_files",
        "open_file",
        "import_model",
        "get_document_info",
        "get_model_statistics",
        "get_model_tree",
        "get_item_properties",
        "batch_get_properties",
        "find_items",
        "find_items_by_name",
        "get_current_selection",
        "clear_selection",
        "select_items_by_search",
        "list_sets",
        "get_selection_set_items",
        "execute_search_set",
        "list_viewpoints",
        "get_current_viewpoint",
        "goto_viewpoint",
        "save_viewpoint",
        "hide_items",
        "unhide_all",
        "resolve_ids",
        "list_models",
        "get_document_metadata",
        "peek_file_info",
        "get_scene_bounds",
        "get_item_hierarchy",
        "get_items_geometry",
        "get_visibility_state",
        "describe_properties",
        "property_value_histogram",
        "export_properties_table",
        "get_hyperlinks",
        "get_grids",
        "locate_items_on_grid",
        "get_comments",
        "list_sheets",
        "list_alignments",
        "send_code",
        "run_baked_tool",
        "apply_bake"
    };
}
