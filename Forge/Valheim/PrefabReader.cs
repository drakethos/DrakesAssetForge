using System.Numerics;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace DrakesForge.Valheim;

/// <summary>Walks a vanilla prefab's hierarchy: renderers, materials, shaders, LODs, scripts, icon.</summary>
public static class PrefabReader
{
    public static PrefabInfo Inspect(AssetSession session, VanillaEntry entry) =>
        session.Run(s => new Walker(s).Read(entry));

    /// <summary>Just the icon sprite: reads the root's scripts only (thumbnails for hundreds of tiles).</summary>
    internal static AssetRef? FindIcon(AssetSession session, VanillaEntry entry) =>
        session.Run(s => new Walker(s).IconOnly(entry));

    private sealed class Node
    {
        public required string Path { get; init; }
        public required bool Active { get; init; }
        /// <summary>Active if the item were worn: Valheim enables the inactive attach_skin child on the player.</summary>
        public bool ActiveWorn { get; init; }
        public int Tag { get; init; }
        public required Matrix4x4 ToRoot { get; init; }
        public required List<AssetExternal> Components { get; init; }
    }

    private sealed class Walker
    {
        private readonly AssetSession _session;
        private readonly AssetsManager _am;
        private readonly Dictionary<(string, long), MaterialInfo> _materials = new();
        private readonly Dictionary<(string, long), string> _shaders = new();
        private readonly Dictionary<(string, long), string> _scriptNames = new();

        public Walker(AssetSession session)
        {
            _session = session;
            _am = session.Manager;
        }

        public PrefabInfo Read(VanillaEntry entry)
        {
            var (file, info) = _session.OpenPrefab(entry);
            var rootGo = _am.GetBaseField(file, info);

            var nodes = new List<Node>();
            Walk(file, rootGo, "", Matrix4x4.Identity, true, true, isRoot: true, nodes);

            var lowerLods = LowerLodRenderers(nodes);
            var renderers = new List<RendererInfo>();
            foreach (var node in nodes)
                foreach (var component in node.Components)
                    if (ReadRenderer(node, component, lowerLods) is { } r)
                        renderers.Add(r);

            var scripts = new List<string>();
            var components = new List<ComponentInfo>();
            AssetRef? icon = null;
            PieceCost? cost = null;
            MaterialInfo? armor = null;
            foreach (var component in nodes[0].Components)
            {
                if ((AssetClassID)component.info.TypeId != AssetClassID.MonoBehaviour)
                    continue;
                var name = ScriptName(component);
                if (name == null)
                    continue;
                scripts.Add(name);
                icon ??= IconOf(name, component);
                components.Add(new ComponentInfo { Name = name, Fields = _session.Components.Read(name, component.baseField) });
                if (name == "Piece")
                    cost = ReadPieceCost(component);
                if (name == "ItemDrop" && component.baseField["m_itemData"]["m_shared"]["m_armorMaterial"] is { IsDummy: false } armorPtr && armorPtr["m_PathID"].AsLong != 0)
                    armor = ReadMaterial(component.file, armorPtr);
            }

            return new PrefabInfo
            {
                Name = rootGo["m_Name"].AsString,
                Scripts = scripts,
                Renderers = renderers,
                HasIcon = icon != null,
                IconSprite = icon,
                PieceCost = cost,
                ArmorMaterial = armor,
                Components = components,
                SnapPoints = nodes
                    .Where(n => n.Path.Length > 0 && !n.Path.Contains('/') && (n.Tag == SnapPointTag || n.Path.Contains("snappoint", StringComparison.OrdinalIgnoreCase)))
                    .Select(n => n.ToRoot.Translation)
                    .ToList()
            };
        }

        // Armour/capes keep the on-body mesh under this (inactive) child; Valheim skins it onto the player when equipped.
        private const string WornRoot = "attach_skin";

        // Unity tag id Valheim gives "snappoint" children ("$hud_snappoint_top", inactive).
        private const int SnapPointTag = 20000;

        // Valheim's Piece.PieceCategory, named the way Jotunn's PieceConfig.Category expects.
        private static readonly string[] Categories = { "Misc", "Crafting", "Building", "HeavyBuild", "Furniture" };

        private PieceCost? ReadPieceCost(AssetExternal piece)
        {
            try
            {
                var bf = piece.baseField;
                var category = bf["m_category"].IsDummy ? 0 : bf["m_category"].AsInt;
                var resources = new List<(string, int, int, bool)>();
                foreach (var r in bf["m_resources.Array"].Children)
                {
                    var item = GameObjectName(piece.file, r["m_resItem"]);
                    if (item != null)
                        resources.Add((item, r["m_amount"].AsInt, r["m_amountPerLevel"].AsInt, r["m_recover"].AsBool));
                }

                return new PieceCost
                {
                    Category = category >= 0 && category < Categories.Length ? Categories[category] : "Misc",
                    Station = GameObjectName(piece.file, bf["m_craftingStation"]),
                    Resources = resources
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Name of the GameObject a component pointer (ItemDrop, CraftingStation, …) sits on.</summary>
        private string? GameObjectName(AssetsFileInstance file, AssetTypeValueField componentPtr)
        {
            if (componentPtr.IsDummy || componentPtr["m_PathID"].AsLong == 0)
                return null;
            var component = _session.Ext(file, componentPtr);
            if (component.baseField == null)
                return null;
            var go = _session.Ext(component.file, component.baseField["m_GameObject"]);
            return go.baseField?["m_Name"].AsString;
        }

        public AssetRef? IconOnly(VanillaEntry entry)
        {
            var (file, info) = _session.OpenPrefab(entry);
            foreach (var c in _am.GetBaseField(file, info)["m_Component.Array"].Children)
            {
                var component = _session.Ext(file, c["component"], true);
                if (component.info == null || (AssetClassID)component.info.TypeId != AssetClassID.MonoBehaviour)
                    continue;
                component = _session.Ext(file, c["component"]);
                if (component.baseField != null && ScriptName(component) is { } name && IconOf(name, component) is { } icon)
                    return icon;
            }

            return null;
        }

        private void Walk(AssetsFileInstance file, AssetTypeValueField go, string path, Matrix4x4 parentToRoot, bool parentActive, bool parentActiveWorn, bool isRoot, List<Node> nodes)
        {
            var components = new List<AssetExternal>();
            AssetExternal? transform = null;
            foreach (var c in go["m_Component.Array"].Children)
            {
                var ext = _session.Ext(file, c["component"]);
                if (ext.info == null || ext.baseField == null)
                    continue;
                var type = (AssetClassID)ext.info.TypeId;
                if (type is AssetClassID.Transform or AssetClassID.RectTransform)
                    transform = ext;
                else
                    components.Add(ext);
            }

            var selfActive = isRoot || go["m_IsActive"].IsDummy || go["m_IsActive"].AsBool;
            var active = parentActive && selfActive;
            var activeWorn = parentActiveWorn && (selfActive || path == WornRoot);
            // The root's own transform is where it's placed in the world; everything is relative to it.
            var toRoot = isRoot || transform == null ? Matrix4x4.Identity : LocalMatrix(transform.Value.baseField) * parentToRoot;
            nodes.Add(new Node { Path = path, Active = active, ActiveWorn = activeWorn, ToRoot = toRoot, Components = components, Tag = go["m_Tag"].IsDummy ? 0 : go["m_Tag"].AsInt });

            if (transform == null)
                return;
            foreach (var childPtr in transform.Value.baseField["m_Children.Array"].Children)
            {
                var child = _session.Ext(transform.Value.file, childPtr);
                if (child.baseField == null)
                    continue;
                var childGo = _session.Ext(child.file, child.baseField["m_GameObject"]);
                if (childGo.baseField == null)
                    continue;
                var name = childGo.baseField["m_Name"].AsString;
                Walk(childGo.file, childGo.baseField, path.Length == 0 ? name : path + "/" + name, toRoot, active, activeWorn, false, nodes);
            }
        }

        private static Matrix4x4 LocalMatrix(AssetTypeValueField t)
        {
            var p = t["m_LocalPosition"];
            var r = t["m_LocalRotation"];
            var s = t["m_LocalScale"];
            return Matrix4x4.CreateScale(s["x"].AsFloat, s["y"].AsFloat, s["z"].AsFloat)
                   * Matrix4x4.CreateFromQuaternion(new Quaternion(r["x"].AsFloat, r["y"].AsFloat, r["z"].AsFloat, r["w"].AsFloat))
                   * Matrix4x4.CreateTranslation(p["x"].AsFloat, p["y"].AsFloat, p["z"].AsFloat);
        }

        /// <summary>Renderers that only appear in LOD1+ (drawing them too would stack every LOD level).</summary>
        private HashSet<(string, long)> LowerLodRenderers(List<Node> nodes)
        {
            var lod0 = new HashSet<(string, long)>();
            var lower = new HashSet<(string, long)>();
            foreach (var component in nodes.SelectMany(n => n.Components))
            {
                if ((AssetClassID)component.info.TypeId != AssetClassID.LODGroup)
                    continue;
                var lods = component.baseField["m_LODs.Array"].Children;
                for (var i = 0; i < lods.Count; i++)
                {
                    foreach (var r in lods[i]["renderers.Array"].Children)
                    {
                        var ext = _session.Ext(component.file, r["renderer"], true);
                        if (ext.info != null)
                            (i == 0 ? lod0 : lower).Add((ext.file.name, ext.info.PathId));
                    }
                }
            }

            lower.ExceptWith(lod0);
            return lower;
        }

        private RendererInfo? ReadRenderer(Node node, AssetExternal component, HashSet<(string, long)> lowerLods)
        {
            var type = (AssetClassID)component.info.TypeId;
            if (type is not (AssetClassID.MeshRenderer or AssetClassID.SkinnedMeshRenderer))
                return null;

            var bf = component.baseField;
            AssetExternal mesh = default;
            if (type == AssetClassID.SkinnedMeshRenderer)
            {
                mesh = _session.Ext(component.file, bf["m_Mesh"], true);
            }
            else
            {
                // MeshRenderer's mesh lives on the sibling MeshFilter.
                var filter = node.Components.FirstOrDefault(c => (AssetClassID)c.info.TypeId == AssetClassID.MeshFilter);
                if (filter.baseField != null)
                    mesh = _session.Ext(filter.file, filter.baseField["m_Mesh"], true);
            }

            var materials = bf["m_Materials.Array"].Children
                .Select(m => ReadMaterial(component.file, m))
                .ToList();

            var enabled = bf["m_Enabled"].IsDummy || bf["m_Enabled"].AsBool;
            return new RendererInfo
            {
                Path = node.Path,
                Skinned = type == AssetClassID.SkinnedMeshRenderer,
                Visible = node.Active && enabled && !lowerLods.Contains((component.file.name, component.info.PathId)),
                Worn = node.Path == WornRoot || node.Path.StartsWith(WornRoot + "/", StringComparison.Ordinal),
                VisibleWorn = node.ActiveWorn && enabled && !lowerLods.Contains((component.file.name, component.info.PathId))
                              && (node.Path == WornRoot || node.Path.StartsWith(WornRoot + "/", StringComparison.Ordinal)),
                ToRoot = node.ToRoot,
                MeshName = mesh.info != null ? AssetName(mesh) : "(none)",
                Mesh = mesh.info != null ? new AssetRef(mesh.file!, mesh.info.PathId) : null,
                Materials = materials
            };
        }

        private MaterialInfo ReadMaterial(AssetsFileInstance file, AssetTypeValueField ptr)
        {
            var ext = _session.Ext(file, ptr);
            if (ext.info == null || ext.baseField == null)
                return new MaterialInfo { Name = "(missing)", Shader = "", Color = new[] { 1f, 0f, 1f, 1f } };

            var key = (ext.file.name, ext.info.PathId);
            if (_materials.TryGetValue(key, out var cached))
                return cached;

            var bf = ext.baseField;
            var props = bf["m_SavedProperties"];
            var color = new[] { 1f, 1f, 1f, 1f };
            foreach (var c in props["m_Colors.Array"].Children)
            {
                if (c["first"].AsString != "_Color")
                    continue;
                var v = c["second"];
                color = new[] { v["r"].AsFloat, v["g"].AsFloat, v["b"].AsFloat, v["a"].AsFloat };
            }

            var floats = new Dictionary<string, float>();
            foreach (var f in props["m_Floats.Array"].Children)
                floats[f["first"].AsString] = f["second"].AsFloat;

            var slots = new List<string>();
            var textures = new Dictionary<string, string>();
            var textureRefs = new Dictionary<string, AssetRef>();
            AssetExternal mainTex = default;
            foreach (var t in props["m_TexEnvs.Array"].Children)
            {
                var name = t["first"].AsString;
                slots.Add(name);
                var tex = _session.Ext(ext.file, t["second"]["m_Texture"], true);
                if (tex.info == null)
                    continue;
                textures[name] = AssetName(tex);
                textureRefs[name] = new AssetRef(tex.file!, tex.info.PathId);
                if (name == "_MainTex")
                    mainTex = tex;
            }

            var material = new MaterialInfo
            {
                Name = bf["m_Name"].AsString,
                Shader = ShaderName(ext.file, bf["m_Shader"]),
                Color = color,
                Floats = floats,
                TextureSlots = slots,
                Textures = textures,
                TextureRefs = textureRefs,
                MainTextureName = mainTex.info != null ? AssetName(mainTex) : null,
                MainTexture = mainTex.info != null ? new AssetRef(mainTex.file!, mainTex.info.PathId) : null
            };
            _materials[key] = material;
            return material;
        }

        private string ShaderName(AssetsFileInstance file, AssetTypeValueField ptr)
        {
            var info = _session.Ext(file, ptr, true);
            if (info.info == null)
                return "";
            var key = (info.file.name, info.info.PathId);
            if (_shaders.TryGetValue(key, out var name))
                return name;

            try
            {
                name = _session.Ext(file, ptr).baseField["m_ParsedForm"]["m_Name"].AsString;
            }
            catch (Exception)
            {
                name = "(unreadable shader)";
            }

            _shaders[key] = name;
            return name;
        }

        private string? ScriptName(AssetExternal behaviour)
        {
            var script = _session.Ext(behaviour.file, behaviour.baseField["m_Script"], true);
            if (script.info == null)
                return null;
            var key = (script.file.name, script.info.PathId);
            if (!_scriptNames.TryGetValue(key, out var name))
            {
                name = _session.Ext(behaviour.file, behaviour.baseField["m_Script"]).baseField?["m_ClassName"].AsString ?? "";
                _scriptNames[key] = name;
            }

            return name.Length == 0 ? null : name;
        }

        private AssetRef? IconOf(string scriptName, AssetExternal behaviour)
        {
            AssetTypeValueField? ptr = scriptName switch
            {
                "ItemDrop" => behaviour.baseField["m_itemData"]["m_shared"]["m_icons.Array"].Children.FirstOrDefault(),
                "Piece" => behaviour.baseField["m_icon"],
                _ => null
            };
            if (ptr == null || ptr.IsDummy)
                return null;
            var sprite = _session.Ext(behaviour.file, ptr, true);
            return sprite.info != null ? new AssetRef(sprite.file, sprite.info.PathId) : null;
        }

        private string AssetName(AssetExternal ext)
        {
            try
            {
                var bf = ext.baseField ?? _am.GetBaseField(ext.file, ext.info);
                return bf["m_Name"].AsString;
            }
            catch (Exception)
            {
                return $"#{ext.info.PathId}";
            }
        }
    }
}
