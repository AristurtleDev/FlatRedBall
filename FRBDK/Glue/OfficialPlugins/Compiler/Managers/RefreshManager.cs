﻿using FlatRedBall.Glue.Controls;
using FlatRedBall.Glue.Elements;
using FlatRedBall.Glue.IO;
using FlatRedBall.Glue.Managers;
using FlatRedBall.Glue.Plugins;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using FlatRedBall.Glue.SaveClasses;
using FlatRedBall.IO;
using Newtonsoft.Json;
using OfficialPlugins.Compiler.CommandSending;
using OfficialPlugins.Compiler.Dtos;
using OfficialPlugins.Compiler.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GeneralResponse = ToolsUtilities.GeneralResponse;
using Xceed.Wpf.Toolkit.PropertyGrid.Attributes;

namespace OfficialPlugins.Compiler.Managers
{
    #region Classes

    public class ExpiringFilePath
    {
        public DateTimeOffset? Expiration { get; set; }
        public FilePath FilePath { get; set; }
    }

    #endregion

    public class RefreshManager : Singleton<RefreshManager>
    {
        #region Fields/Properties

        Action<string> printOutput;
        Action<string> printError;
        string screenToRestartOn = null;


        bool isExplicitlySetRebuildAndRestartEnabled;
        public bool IsExplicitlySetRebuildAndRestartEnabled 
        {
            get => isExplicitlySetRebuildAndRestartEnabled;
            set
            {
                isExplicitlySetRebuildAndRestartEnabled = value;
                RefreshViewModelHotReload();

            }
        }
        bool failedToRebuildAndRestart { get; set; }

        public bool ShouldRestartOnChange => 
            (failedToRebuildAndRestart || IsExplicitlySetRebuildAndRestartEnabled || 
                (ViewModel.IsRunning && ViewModel.IsEditChecked)) &&
            GlueState.Self.CurrentGlueProject != null;

        public int PortNumber { get; set; }

        public CompilerViewModel ViewModel
        {
            get; set;
        }
        public GlueViewSettingsViewModel GlueViewSettingsViewModel
        {
            get; set;
        }

        public bool IgnoreNextObjectAdd { get; set; }
        public bool IgnoreNextObjectSelect { get; set; }

        public SynchronizedCollection<ExpiringFilePath> FilePathsToIgnore { get; private set; }
            = new SynchronizedCollection<ExpiringFilePath>();

        #endregion

        #region Initialize

        public void InitializeEvents(Action<string> printOutput, Action<string> printError)
        {
            this.printOutput = printOutput;
            this.printError = printError;
        }

        #endregion

        #region Utilities

        public string GetGameTypeFor(GlueElement element)
        {
            if(element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            else if(element.Name == null)
            {
                throw new NullReferenceException(nameof(element.Name));
            }

            return
                GlueState.Self.ProjectNamespace + "." + element.Name.Replace("\\", ".");
        }

        #endregion

        #region File

        private void RemoveExpiredPaths()
        {
            var toRemove = FilePathsToIgnore.Where(item => item.Expiration < DateTime.Now).ToArray();

            foreach(var item in toRemove)
            {
                FilePathsToIgnore.Remove(item);
            }
        }

        public async void HandleFileChanged(FilePath fileName)
        {
            // always do this:
            RemoveExpiredPaths();
            var found =
                FilePathsToIgnore.FirstOrDefault(item => item.FilePath == fileName);
            if (found != null)
            {
                if(DateTime.Now > found.Expiration)
                {
                    FilePathsToIgnore.Remove(found);
                }
                else
                {
                    printOutput($"Ignoring file change {fileName}");
                }
            }

            var shouldReactToFileChange =
                found == null &&
                ShouldRestartOnChange &&
                GetIfShouldReactToFileChange(fileName);

            if(shouldReactToFileChange)
            {
                var rfses = GlueCommands.Self.FileCommands.GetReferencedFiles(fileName.FullPath);
                var firstRfs = rfses.FirstOrDefault();
                var isGlobalContent = rfses.Any(item => item.GetContainer() == null);

                bool canSendCommands = ViewModel.IsGenerateGlueControlManagerInGame1Checked;

                var handled = false;

                if(canSendCommands)
                {
                    string strippedName = null;
                    if (firstRfs != null)
                    {
                        strippedName = FileManager.RemovePath(FileManager.RemoveExtension(firstRfs.Name));
                    }
                    else
                    {
                        strippedName = fileName.NoPath;
                    }
                    if(isGlobalContent && firstRfs.GetAssetTypeInfo().CustomReloadFunc != null)
                    {
                        printOutput($"Waiting for Glue to copy reload global file {strippedName}");

                        // just give the file time to copy:
                        await Task.Delay(500);

                        // it's part of global content and can be reloaded, so let's just tell
                        // it to reload:
                        await CommandSender.Send(new ReloadGlobalContentDto
                        {
                            StrippedGlobalContentFileName = strippedName
                        });

                        printOutput($"Reloading global file {strippedName}");

                        handled = true;
                    }

                    var containerNames = rfses.Select(item => item.GetContainer()?.Name).Where(item => item != null).ToHashSet();

                    var shouldCopy = false;
                    shouldCopy = containerNames.Any() || GlueCommands.Self.FileCommands.IsContent(fileName);

                    if (shouldCopy)
                    {
                        // Right now we'll assume the screen owns this file, although it is possible that it's 
                        // global but not part of global content. That's a special case we'll have to handle later
                        printOutput($"Waiting for file to be copied: {strippedName}");
                        await Task.Delay(600);
                        try
                        {
                            if(ViewModel.IsRunning)
                            {
                                var extension = fileName.Extension;
                                var shouldReload = extension == "csv";
                                if(shouldReload)
                                {
                                    printOutput($"Sending force reload for file: {strippedName}");

                                    var dto = new Dtos.ForceReloadFileDto();
                                    dto.ElementsContainingFile = containerNames.ToList();
                                    dto.StrippedFileName = fileName.NoPathNoExtension;
                                    await CommandSender.Send(dto);
                                }
                                else
                                {
                                    printOutput($"Telling game to restart screen");

                                    await CommandSender.Send(new RestartScreenDto());
                                }
                            }

                            handled = true;
                        }
                        catch(Exception e)
                        {
                            printError($"Error trying to send command:{e.ToString()}");
                        }
                    }
                }
                if(!handled)
                {
                    StopAndRestartTask($"File {fileName} changed");
                }
            }
        }

        internal bool HandleTreeNodeDoubleClicked(TreeNode arg)
        {
            if(arg.Tag is NamedObjectSave asNos)
            {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                PushGlueSelectionToGame(bringIntoFocus:true);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                return true;
            }
            return false;
        }

        private bool GetIfShouldReactToFileChange(FilePath filePath )
        {
            var noPath = filePath.NoPath;
            if(filePath.FullPath.Contains(".Generated.") && filePath.FullPath.EndsWith(".cs"))
            {
                return false;
            }
            if(filePath.FullPath.EndsWith(".Generated.xml"))
            {
                return false;
            }
            if(noPath == "CompilerSettings.json")
            {
                return false;
            }



            return true;
        }

        internal void HandleNewFile(ReferencedFileSave newFile)
        {
            GlueCommands.Self.ProjectCommands.CopyToBuildFolder(newFile);
        }

        #endregion

        #region Entity Created

        internal async void HandleNewEntityCreated(EntitySave newEntity)
        {
            if(ViewModel.IsRunning && ViewModel.IsEditChecked)
            {
                var filePath = GlueCommands.Self.FileCommands.GetCustomCodeFilePath(newEntity);


                IgnoreNextChange(filePath);

                var dto = new CreateNewEntityDto();
                dto.EntitySave = newEntity;

                await CommandSender.Send(dto);

                // selection happens before the entity is created, so let's force push the selection to the game
                await PushGlueSelectionToGame();
            }
        }

        public void IgnoreNextChange(FilePath filePath)
        {
            const int responseDelay = 5;
            var expiringFilePath =
                new ExpiringFilePath
                {
                    Expiration = DateTime.Now + TimeSpan.FromSeconds(responseDelay),
                    FilePath = filePath
                };

            printOutput($"Ignoring {expiringFilePath.FilePath} for {responseDelay} seconds");
            FilePathsToIgnore.Add(expiringFilePath);
        }

        #endregion

        #region Screen Created

        internal void HandleNewScreenCreated()
        {
            if (ShouldRestartOnChange)
            {
                StopAndRestartTask($"New screen created");
            }
        }

        #endregion

        #region State Created

        internal async void HandleStateCreated(StateSave state, StateSaveCategory category)
        {
            if(category != null)
            {
                var container = ObjectFinder.Self.GetElementContaining(category);

                var dto = new CreateNewStateDto();
                dto.StateSave = state;
                dto.CategoryName = category?.Name;
                dto.ElementNameGame = GetGameTypeFor(container);

                await CommandSender.Send(dto);
            }
        }

        #endregion

        #region NamedObject Created

        internal async void HandleNewObjectCreated(NamedObjectSave newNamedObject)
        {
            if(IgnoreNextObjectAdd)
            {
                IgnoreNextObjectAdd = false;
            }
            else if (ViewModel.IsRunning && ViewModel.IsEditChecked)
            {
                var tempSerialized = JsonConvert.SerializeObject(newNamedObject);
                var addObjectDto = JsonConvert.DeserializeObject<AddObjectDto>(tempSerialized);
                var containerElement = ObjectFinder.Self.GetElementContaining(newNamedObject);
                if (containerElement != null)
                {
                    addObjectDto.ElementNameGame = GetGameTypeFor(containerElement);
                }

                var addResponseAsString = await CommandSender.Send(addObjectDto);

                AddObjectDtoResponse addResponse = null;
                if(!string.IsNullOrEmpty(addResponseAsString))
                {
                    try
                    {
                        addResponse = JsonConvert.DeserializeObject<AddObjectDtoResponse>(addResponseAsString);
                    }
                    catch(Exception e)
                    {
                        printOutput($"Error parsing string:\n\n{addResponseAsString}");
                    }
                }

                if(addResponse?.WasObjectCreated == true)
                {
                    var isPositionedObject = newNamedObject.SourceType == SourceType.Entity ||
                        (newNamedObject.GetAssetTypeInfo()?.IsPositionedObject == true);
                    if(isPositionedObject)
                    {
                        await AdjustNewObjectToCameraPosition(newNamedObject);
                    }
                }
                else
                {
                    StopAndRestartTask($"Restarting because of added object {newNamedObject}");
                }
            }
        }

        private async Task AdjustNewObjectToCameraPosition(NamedObjectSave newNamedObject)
        {
            if (GlueState.Self.CurrentScreenSave != null)
            {
                // If it's in a screen, then we position the object on the camera:

                var cameraPosition = Microsoft.Xna.Framework.Vector3.Zero;

                cameraPosition = await CommandSender.GetCameraPosition(PortNumber);

                var gluxCommands = GlueCommands.Self.GluxCommands;

                bool didSetValue = false;

                Vector2 newPosition = new Vector2(cameraPosition.X, cameraPosition.Y);

                var list = GlueState.Self.CurrentElement.NamedObjects.FirstOrDefault(item =>
                    item.ContainedObjects.Contains(newNamedObject));

                var shouldIncreasePosition = false;
                do
                {
                    shouldIncreasePosition = false;

                    var listToLoopThrough = list?.ContainedObjects ?? GlueState.Self.CurrentElement.NamedObjects;

                    const int incrementForNewObject = 16;
                    const int minimumDistanceForObjects = 3;
                    foreach (var item in listToLoopThrough)
                    {
                        if (item != newNamedObject)
                        {
                            Vector2 itemPosition = new Vector2(
                                (item.GetCustomVariable("X")?.Value as float?) ?? 0,
                                (item.GetCustomVariable("Y")?.Value as float?) ?? 0);

                            var distance = (itemPosition - newPosition).Length();


                            if (distance < minimumDistanceForObjects)
                            {
                                shouldIncreasePosition = true;
                                break;
                            }

                        }
                    }
                    if (shouldIncreasePosition)
                    {
                        newPosition.X += incrementForNewObject;
                    }

                } while (shouldIncreasePosition);

                if (newPosition.X != 0)
                {
                    gluxCommands.SetVariableOn(newNamedObject, "X", newPosition.X);
                    didSetValue = true;
                }
                if (newPosition.Y != 0)
                {
                    gluxCommands.SetVariableOn(newNamedObject, "Y", newPosition.Y);

                    didSetValue = true;
                }



                if (didSetValue)
                {
                    GlueCommands.Self.GenerateCodeCommands.GenerateCurrentElementCode();
                    GlueCommands.Self.RefreshCommands.RefreshPropertyGrid();
                    GlueCommands.Self.GluxCommands.SaveGlux();
                }
            }
        }

        #endregion

        #region Variable Created

        internal async void HandleVariableAdded(CustomVariable newVariable)
        {
            // Vic says - When a new variable is added, we don't need to restart. However,
            // later that variable might get assigned on instances of an object, and if it is
            // then that would probably fail because it would attempt to assign through reflection.
            // Therefore, we could either restart here so that all future assignments work, or we could
            // restart on the variable set. While it might result in fewer restarts to restart when the
            // variable is assigned (since the variable may not actually get assigned in Glue), it could
            // also lead to confusion. Therefore, we'll just restart here:
            // Update August 21, 2021
            // Let's look at the possible variables that are added:
            // * New variables - which by default have no functionality until code is written for them
            // * Exposed variables - these do have functionality but they ultimately are just setting other variables
            // If it's a new variable, we are going to restart. Otherwise if it's expoed, send that to the game to use 
            // for assigning real values
            var isTunneled = !string.IsNullOrWhiteSpace(newVariable.SourceObject) &&
                !string.IsNullOrWhiteSpace(newVariable.SourceObjectProperty);

            if(isTunneled)
            {
                // send this down to the game
                var dto = new AddVariableDto();
                dto.CustomVariable = newVariable;
                dto.ElementGameType = GetGameTypeFor(GlueState.Self.CurrentElement);

                await CommandSender.Send(dto);
            }
            else
            {
                // it's a brand new variable, so let's restart it...
                StopAndRestartTask($"Restarting because of added variable {newVariable}");
            }
        }

        #endregion

        #region Selected Object

        internal async void HandleItemSelected(TreeNode selectedTreeNode)
        {
            if(IgnoreNextObjectSelect)
            {
                IgnoreNextObjectSelect = false;
            }
            else if(ViewModel.IsEditChecked)
            {
                await PushGlueSelectionToGame();
            }

        }

        public async Task PushGlueSelectionToGame(string forcedCategoryName = null, string forcedStateName = null, GlueElement forcedElement = null, bool bringIntoFocus = false)
        {
            var element = forcedElement ?? GlueState.Self.CurrentElement;

            var dto = new SelectObjectDto();

            NamedObjectSave nos = null;
            if(forcedElement == null)
            {
                nos = GlueState.Self.CurrentNamedObjectSave;
            }
            if(element != null)
            {
                dto.ScreenSave = element as ScreenSave;
                dto.EntitySave = element as EntitySave;
                dto.BringIntoFocus = bringIntoFocus;
                dto.NamedObject = nos;
                dto.ElementNameGlue = element.Name;
                dto.StateName = forcedStateName ??
                    GlueState.Self.CurrentStateSave?.Name;

                dto.StateCategoryName = forcedCategoryName ??
                    GlueState.Self.CurrentStateSaveCategory?.Name;

                await CommandSender.Send(dto);
            }

        }

        #endregion

        #region Variable Changed


        internal void HandleNamedObjectValueChanged(string variableName, object oldValue, NamedObjectSave nos, AssignOrRecordOnly assignOrRecordOnly)
        {
            var foundVariable = nos.GetCustomVariable(variableName);
            if(ViewModel.IsRunning && ViewModel.IsEditChecked)
            {
                VariableSendingManager.Self.HandleNamedObjectValueChanged(variableName, oldValue, nos, assignOrRecordOnly);
            }
        }

        internal void HandleVariableChanged(IElement variableElement, CustomVariable variable)
        {
            if (ViewModel.IsRunning && ViewModel.IsEditChecked)
            {
                VariableSendingManager.Self.HandleVariableChanged(variableElement, variable);
            }
        }


        #endregion

        #region State Variable Changed

        internal async void HandleStateVariableChanged(StateSave state, StateSaveCategory category, string variableName)
        {
            var container = ObjectFinder.Self.GetElementContaining(category);

            if(container != null)
            {
                var dto = new ChangeStateVariableDto();
                dto.StateSave = state;
                dto.CategoryName = category?.Name;
                dto.ElementNameGame = GetGameTypeFor(container);
                dto.VariableName = variableName;

                await CommandSender.Send(dto);
            }

        }

        #endregion

        #region Object Container (List, Layer, ShapeCollection) changed
        internal async void HandleObjectContainerChanged(NamedObjectSave objectMoving, 
            NamedObjectSave newContainer)
        {
            if (ViewModel.IsRunning && ViewModel.IsEditChecked)
            {
                GeneralResponse generalResponse = GeneralResponse.UnsuccessfulWith("Unknown error");
                string responseAsString = null;
                var element = ObjectFinder.Self.GetElementContaining(objectMoving);

                if(element == null)
                {
                    generalResponse = GeneralResponse.UnsuccessfulWith("Could not find Glue Element containing {objectMoving}");
                }
                else
                {
                    var dto = new MoveObjectToContainerDto
                    {
                        ElementName = element.Name,
                        ObjectName = objectMoving.InstanceName,
                        ContainerName = newContainer?.InstanceName

                    };

                    responseAsString = await CommandSender.Send(dto);

                    if(string.IsNullOrEmpty(responseAsString))
                    {
                        generalResponse = GeneralResponse.UnsuccessfulWith($"Sent the command to move {objectMoving} to {newContainer} but never got a response from the game");
                    }
                    else
                    {
                        try
                        {
                            var response = JsonConvert.DeserializeObject<MoveObjectToContainerDtoResponse>(responseAsString);
                            if(response.WasObjectMoved)
                            {
                                generalResponse = GeneralResponse.SuccessfulResponse;
                            }
                            else
                            {
                                generalResponse = GeneralResponse.UnsuccessfulWith($"Failed to move object to game. No extra info provided by game.");
                            }
                        }
                        catch(Exception e)
                        {
                            generalResponse = GeneralResponse.UnsuccessfulWith($"Failed to deserialie response:\n\n{responseAsString}\n\n{e}");
                        }
                    }
                }

                if(!generalResponse.Succeeded)
                {
                    StopAndRestartTask($"Restarting due to changed container for {objectMoving}\n{generalResponse.Message}");
                }
            }
        }
        #endregion

        #region Object Removed
        internal async Task HandleObjectRemoved(IElement owner, NamedObjectSave nos)
        {
            if (ViewModel.IsRunning && ViewModel.IsEditChecked)
            {
                var dto = new Dtos.RemoveObjectDto();

                dto.ScreenSave = GlueState.Self.CurrentElement as ScreenSave;
                dto.EntitySave = GlueState.Self.CurrentElement as EntitySave;

                dto.ElementNameGlue = //ToGameType((GlueElement)owner);
                    owner.Name;
                dto.ObjectName = nos.InstanceName;
                var timeBeforeSend = DateTime.Now;
                var responseAsstring = await CommandSender.Send(dto);
                var timeAfterSend = DateTime.Now;
                printOutput($"Delete send took {timeAfterSend - timeBeforeSend}\n \n ");
                RemoveObjectDtoResponse response = null;
                try
                {
                    response = JsonConvert.DeserializeObject<RemoveObjectDtoResponse>(responseAsstring);
                }
                catch (Exception e)
                {
                    printOutput($"Error parsing response from game:\n\n{responseAsstring}");
                }
                if(response == null || (response.DidScreenMatch && response.WasObjectRemoved == false))
                {
                    StopAndRestartTask(
                        $"Restarting because {nos} was deleted from Glue but not from game");
                }

            }
        }
        #endregion

        #region Stop/Restart

        const string stopRestartDetails =
                   "Restarting due to Glue or file change";

        bool CanRestart => ViewModel.IsGenerateGlueControlManagerInGame1Checked &&
            (
                Runner.Self.DidRunnerStartProcess || 
                (ViewModel.IsRunning == false && failedToRebuildAndRestart) ||
                (ViewModel.IsRunning && ViewModel.IsEditChecked)
            );

        public async void StopAndRestartTask(string reason)
        {
            if (CanRestart)
            {
                if(!string.IsNullOrEmpty(reason))
                {
                    printOutput($"Restarting because: {reason}. Waiting for tasks to finish...");
                }
                await TaskManager.Self.WaitForAllTasksFinished();
                var wasInEditMode = ViewModel.IsEditChecked;
                await StopAndRestartImmediately(PortNumber);
                if(wasInEditMode)
                {
                    ViewModel.IsEditChecked = true;
                }
            }
        }


        private async Task StopAndRestartImmediately(int portNumber)
        {
            bool DoesTaskManagerHaveAnotherRestartTask()
            {
                var actions = TaskManager.Self.SyncedActions;

                var restartTask = actions.FirstOrDefault(item => item != actions[0] &&
                    item.DisplayInfo == stopRestartDetails);

                return restartTask != null;
            }

            var runner = Runner.Self;
            var compiler = Compiler.Self;

            if(CanRestart)
            {

                if (ViewModel.IsRunning)
                {
                    try
                    {
                        screenToRestartOn = await CommandSending.CommandSender.GetScreenName(portNumber);
                    }
                    catch (AggregateException)
                    {
                        printOutput("Could not get the game's screen, restarting game from startup screen");

                    }
                    catch (SocketException)
                    {
                        // do nothing, may not have been able to communicate, just output
                        printOutput("Could not get the game's screen, restarting game from startup screen");
                    }

                    runner.KillGameProcess();
                }

                bool compileSucceeded = false;
                if(!DoesTaskManagerHaveAnotherRestartTask())
                {
                    compileSucceeded = await compiler.Compile(printOutput, printError);
                }

                if (compileSucceeded)
                {
                    if(!DoesTaskManagerHaveAnotherRestartTask())
                    {
                        var response = await runner.Run(preventFocus: true, runArguments: screenToRestartOn);
                        if(response.Succeeded == false)
                        {
                            printError(response.Message);
                        }
                        failedToRebuildAndRestart = response.Succeeded == false;
                    }
                }
                else
                {
                    failedToRebuildAndRestart = true;
                }
                RefreshViewModelHotReload();
            }

        }

        #endregion

        private void RefreshViewModelHotReload()
        {
            ViewModel.IsHotReloadAvailable = ShouldRestartOnChange;
        }
    }
}
