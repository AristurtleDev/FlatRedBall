﻿using FlatRedBall.Glue.CodeGeneration.CodeBuilder;
using FlatRedBall.Glue.Managers;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using FlatRedBall.Glue.SaveClasses;
using Gum.DataTypes;
using Gum.DataTypes.Variables;
using GumPlugin.CodeGeneration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static FlatRedBall.Glue.SaveClasses.GlueProjectSave;

namespace GumPluginCore.CodeGeneration
{
    internal class TextCodeGenerator : Singleton<TextCodeGenerator>
    {

        public void AddStandardSetterReplacements(Dictionary<string, Action<ICodeBlock>> mStandardSetterReplacements)
        {
            mStandardSetterReplacements.Add("Text", (codeBlock) =>
            {
                //codeBlock.If("this.WidthUnits == Gum.DataTypes.DimensionUnitType.RelativeToChildren")
                //    .Line("// make it have no line wrap width before assignign the text:")
                //    .Line("ContainedText.Width = 0;");

                //codeBlock.Line("ContainedText.RawText = value;");
                //codeBlock.Line("UpdateLayout();");

                codeBlock.Line("var widthBefore = ContainedText.WrappedTextWidth;");
                codeBlock.Line("var heightBefore = ContainedText.WrappedTextHeight;");

                codeBlock.Line("if (this.WidthUnits == Gum.DataTypes.DimensionUnitType.RelativeToChildren)");
                codeBlock.Line("{");
                codeBlock.Line("    // make it have no line wrap width before assignign the text:");
                codeBlock.Line("    ContainedText.Width = 0;");
                codeBlock.Line("}");


                codeBlock.Line("ContainedText.RawText = value;");

                if (GlueState.Self.CurrentGlueProject?.FileVersion >= (int)GluxVersions.GraphicalUiElementINotifyPropertyChanged)
                {
                    codeBlock.Line("NotifyPropertyChanged();");
                }

                codeBlock.Line("var shouldUpdate = widthBefore != ContainedText.WrappedTextWidth || heightBefore != ContainedText.WrappedTextHeight;");

                codeBlock.Line("if (shouldUpdate)");
                codeBlock.Line("{");
                if (GlueState.Self.CurrentGlueProject?.FileVersion >= (int)GluxVersions.GumTextObjectsUpdateTextWith0ChildDepth)
                {
                    codeBlock.Line("    UpdateLayout(Gum.Wireframe.GraphicalUiElement.ParentUpdateType.IfParentWidthHeightDependOnChildren | Gum.Wireframe.GraphicalUiElement.ParentUpdateType.IfParentStacks, 0);");
                }
                else
                {
                    codeBlock.Line("    UpdateLayout(true, int.MaxValue/2);");
                }
                codeBlock.Line("}");
            });

            mStandardSetterReplacements.Add("FontScale", (codeBlock) =>
            {
                codeBlock.Line("ContainedText.FontScale = value;");
                codeBlock.Line("UpdateLayout();");
            });
        }

        public void AddVariableNamesToSkipForProperties(List<string> mVariableNamesToSkipForProperties)
        {
            mVariableNamesToSkipForProperties.Add("IsItalic");
            mVariableNamesToSkipForProperties.Add("IsBold");

            mVariableNamesToSkipForProperties.Add("Font");
            mVariableNamesToSkipForProperties.Add("FontSize");

            mVariableNamesToSkipForProperties.Add("OutlineThickness");
            mVariableNamesToSkipForProperties.Add("UseFontSmoothing");

            if(GlueState.Self.CurrentGlueProject?.FileVersion < (int)GluxVersions.GumTextObjectsHaveTextOverflowProperties)
            {
                mVariableNamesToSkipForProperties.Add("TextOverflowHorizontalMode");
                mVariableNamesToSkipForProperties.Add("TextOverflowVerticalMode");
            }
        }

        public void AddVariableNamesToSkipForStates(List<string> variableNamesToSkipForStates)
        {
            if (GlueState.Self.CurrentGlueProject?.FileVersion < (int)GluxVersions.GumTextObjectsHaveTextOverflowProperties)
            {
                variableNamesToSkipForStates.Add("TextOverflowHorizontalMode");
                variableNamesToSkipForStates.Add("TextOverflowVerticalMode");
            }
        }

        public void GenerateAdditionalMethods(StandardElementSave standardElementSave, ICodeBlock classBodyBlock)
        {
            if (standardElementSave.Name == "Text")
            {
                if (GlueState.Self.CurrentGlueProject.FileVersion >= (int)GlueProjectSave.GluxVersions.GumDefaults2)
                {
                    var overrideTextRenderingPositionModeProperty = classBodyBlock.Property("public RenderingLibrary.Graphics.TextRenderingPositionMode?", "OverrideTextRenderingPositionMode");
                    overrideTextRenderingPositionModeProperty.Line("get => mContainedText.OverrideTextRenderingPositionMode;");
                    overrideTextRenderingPositionModeProperty.Line("set => mContainedText.OverrideTextRenderingPositionMode = value;");
                }
            }
        }

        public void GenerateVariableProperties(StandardElementSave standardElementSave, ICodeBlock currentBlock, string containedGraphicalObjectName)
        {
            if (standardElementSave.Name == "Text")
            {
                // generate text-specific properties here:
                StandardsCodeGenerator.Self.GenerateVariable(currentBlock, containedGraphicalObjectName,
                    new VariableSave { Name = "BitmapFont", Type = "RenderingLibrary.Graphics.BitmapFont" },
                    standardElementSave);

                StandardsCodeGenerator.Self.GenerateVariable(currentBlock, containedGraphicalObjectName,
                    new VariableSave { Name = "WrappedText", Type = "System.Collections.Generic.List<string>" },
                    standardElementSave,
                    generateSetter: false);

            }
        }
    }
}
