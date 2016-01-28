﻿using FlatRedBall.Glue.Elements;
using FlatRedBall.Glue.Managers;
using FlatRedBall.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TileGraphicsPlugin
{
    public class AssetTypeInfoAdder : Singleton<AssetTypeInfoAdder>
    {



        public void UpdateAtiCsvPresence()
        {
            string projectFolder = FileManager.GetDirectory(FlatRedBall.Glue.ProjectManager.GlueProjectFileName);
            string settingsFolder = projectFolder + "GlueSettings/";
            if (!Directory.Exists(settingsFolder))
            {
                Directory.CreateDirectory(settingsFolder);
            }

            List<AssetTypeInfo> list;
            list = new List<AssetTypeInfo>();

            var layeredTileMapScnx = CreateAtiForLayeredTilemapScnx();
            var layeredTilemapTilb = CreateAtiForLayeredTilemapTilb();
            var tileShapeCollectionAti = CreateAtiForTileShapeCollection();

            AddIfNotPresent(GetAtiForRawTmx());
            AddIfNotPresent(layeredTileMapScnx);
            AddIfNotPresent(layeredTilemapTilb);
            AddIfNotPresent(tileShapeCollectionAti);
        }


        public void AddIfNotPresent(AssetTypeInfo ati)
        {
            if (AvailableAssetTypes.Self.AllAssetTypes.Any(item => item.FriendlyName == ati.FriendlyName) == false)
            {
                AvailableAssetTypes.Self.AddAssetType(ati);
            }
        }


        private AssetTypeInfo CreateAtiForLayeredTilemapScnx()
        {
            AssetTypeInfo toReturn = new AssetTypeInfo();

            toReturn.FriendlyName = "LayeredTileMap (.scnx)";
            toReturn.QualifiedRuntimeTypeName = new PlatformSpecificType();
            toReturn.QualifiedRuntimeTypeName.QualifiedType = "FlatRedBall.TileGraphics.LayeredTileMap";
            toReturn.QualifiedSaveTypeName = "FlatRedBall.Content.SpriteEditorScene";
            toReturn.Extension = "scnx";
            toReturn.AddToManagersMethod = new List<string>();
            toReturn.AddToManagersMethod.Add("this.AddToManagers()");
            toReturn.CustomLoadMethod = "{THIS} = FlatRedBall.TileGraphics.LayeredTileMap.FromScene(\"{FILE_NAME}\", {CONTENT_MANAGER_NAME});";
            toReturn.DestroyMethod = "this.Destroy()";
            toReturn.ShouldBeDisposed = false;
            toReturn.ShouldAttach = true;
            toReturn.MustBeAddedToContentPipeline = false;
            toReturn.CanBeCloned = false;
            toReturn.HasCursorIsOn = false;
            toReturn.HasVisibleProperty = false;
            toReturn.CanIgnorePausing = false;
            toReturn.CanBeObject = true;
            // We don't want this to show up as a file type - it's the same thing as a .scnx, and should only be used
            // from a TMX.
            toReturn.HideFromNewFileWindow = true;


            return toReturn;
        }

        private AssetTypeInfo CreateAtiForLayeredTilemapTilb()
        {
            AssetTypeInfo toReturn = CreateAtiForLayeredTilemapScnx();
            toReturn.FriendlyName = "LayeredTileMap (.tilb)";
            toReturn.QualifiedSaveTypeName = "";
            toReturn.Extension = "tilb";
            toReturn.CustomLoadMethod = "{THIS} = FlatRedBall.TileGraphics.LayeredTileMap.FromReducedTileMapInfo(\"{FILE_NAME}\", {CONTENT_MANAGER_NAME});";

            return toReturn;
        }

        private AssetTypeInfo GetAtiForRawTmx()
        {
            AssetTypeInfo toReturn = CreateAtiForLayeredTilemapScnx();
            toReturn.FriendlyName = "LayeredTileMap (.tmx)";
            toReturn.QualifiedSaveTypeName = "";
            toReturn.Extension = "tmx";
            toReturn.CustomLoadMethod = "{THIS} = FlatRedBall.TileGraphics.LayeredTileMap.FromTiledMapSave(\"{FILE_NAME}\", {CONTENT_MANAGER_NAME});";

            return toReturn;
        }

        public AssetTypeInfo CreateAtiForTileShapeCollection()
        {

            AssetTypeInfo toReturn = new AssetTypeInfo();
            toReturn.FriendlyName = "TileShapeCollection";
            toReturn.QualifiedRuntimeTypeName = new PlatformSpecificType();
            toReturn.QualifiedRuntimeTypeName.QualifiedType = "FlatRedBall.TileCollisions.TileShapeCollection";
            toReturn.QualifiedSaveTypeName = null;
            toReturn.Extension = null;
            toReturn.AddToManagersMethod = new List<string>();
            toReturn.CustomLoadMethod = null;
            toReturn.DestroyMethod = "this.Visible = false";
            toReturn.ShouldBeDisposed = false;
            toReturn.ShouldAttach = false;
            toReturn.MustBeAddedToContentPipeline = false;
            toReturn.CanBeCloned = false;
            toReturn.HasCursorIsOn = false;
            toReturn.CanIgnorePausing = false;
            toReturn.CanBeObject = true;
            toReturn.HasVisibleProperty = true;

            return toReturn;
        }

    }
}
