﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows.Data;
using Dynamo.Core;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Wpf.Extensions;

namespace NodePropertyPalette
{
    /// <summary>
    /// ViewModel for NodePropertyPalette. 
    /// Handles profiling setup, workspace events, execution events, etc.
    /// </summary>
    public class NodePropertyPaletteWindowViewModel : NotificationObject, IDisposable
    {
        #region Internal Properties
        private ViewLoadedParams viewLoadedParams;
        private HomeWorkspaceModel currentWorkspace;
        private SynchronizationContext uiContext;

        private HomeWorkspaceModel CurrentWorkspace
        {
            get
            {
                return currentWorkspace;
            }
            set
            {
                // Unsubscribe from old workspace
                if (currentWorkspace != null)
                {
                    UnsubscribeWorkspaceEvents(currentWorkspace);
                }

                // Subscribe to new workspace
                if (value != null)
                {
                    // Set new workspace
                    currentWorkspace = value;
                    SubscribeWorkspaceEvents(currentWorkspace);
                }
            }
        }
        #endregion

        #region Public Properties

        /// <summary>
        /// Collection of profiling data for nodes in the current workspace
        /// </summary>
        public ObservableCollection<PropertyPaletteNodeViewModel> PropertyPaletteNodes { get; set; } = new ObservableCollection<PropertyPaletteNodeViewModel>();

        /// <summary>
        /// Collection of profiling data for nodes in the current workspace.
        /// Profiling data in this collection is grouped by the profiled nodes' states.
        /// </summary>
        public CollectionViewSource PropertyPaletteNodesCollection { get; set; }

        /// <summary>
        /// Selected bulk operation to apply.
        /// </summary>
        public BulkOperation BulkOperation { get; set; }
        #endregion

        #region Constructor

        public NodePropertyPaletteWindowViewModel(ViewLoadedParams p)
        {
            viewLoadedParams = p;
            // Saving UI context so later when we touch the collection, it is still performed in the same context
            uiContext = SynchronizationContext.Current;
            p.CurrentWorkspaceChanged += OnCurrentWorkspaceChanged;
            p.CurrentWorkspaceCleared += OnCurrentWorkspaceCleared;

            if (p.CurrentWorkspaceModel is HomeWorkspaceModel)
            {
                CurrentWorkspace = p.CurrentWorkspaceModel as HomeWorkspaceModel;
            }
            PropertyPaletteNodesCollection = new CollectionViewSource();
            PropertyPaletteNodesCollection.Source = PropertyPaletteNodes;
            PropertyPaletteNodesCollection.SortDescriptions.Clear();
            PropertyPaletteNodesCollection.SortDescriptions.Add(new SortDescription(nameof(PropertyPaletteNodeViewModel.NodeType), ListSortDirection.Descending));
            if (PropertyPaletteNodesCollection.View != null)
                PropertyPaletteNodesCollection.View.Refresh();
        }

        #endregion

        #region ExecutionEvents

        private void CurrentWorkspaceModel_EvaluationStarted(object sender, EventArgs e)
        {
        }

        private void CurrentWorkspaceModel_EvaluationCompleted(object sender, Dynamo.Models.EvaluationCompletedEventArgs e)
        {
            // TODO: We may need to update node values after graph execution,
            // Depending on if we display node values at all.
        }

        internal void OnNodeExecutionBegin(NodeModel nm)
        {
            RaisePropertyChanged(nameof(PropertyPaletteNodesCollection));
        }

        internal void OnNodeExecutionEnd(NodeModel nm)
        {
            RaisePropertyChanged(nameof(PropertyPaletteNodesCollection));
        }

        #endregion

        #region Workspace Events

        private void CurrentWorkspaceModel_NodeAdded(NodeModel node)
        {
            // When a new node added on canvas, update PropertyPalette
            var profiledNode = new PropertyPaletteNodeViewModel(node);
            PropertyPaletteNodes.Add(profiledNode);
            RaisePropertyChanged(nameof(PropertyPaletteNodesCollection));
        }

        private void CurrentWorkspaceModel_NodeRemoved(NodeModel node)
        {
            var propertyPaletteNode = PropertyPaletteNodes.Where(n => n.NodeModel == node).FirstOrDefault();
            if (propertyPaletteNode != null)
            {
                PropertyPaletteNodes.Remove(propertyPaletteNode);
                propertyPaletteNode.Dispose();
            }
            RaisePropertyChanged(nameof(PropertyPaletteNodesCollection));
        }

        private void OnCurrentWorkspaceChanged(IWorkspaceModel workspace)
        {
            CurrentWorkspace = workspace as HomeWorkspaceModel;
        }

        private void OnCurrentWorkspaceCleared(IWorkspaceModel workspace)
        {
            CurrentWorkspace = viewLoadedParams.CurrentWorkspaceModel as HomeWorkspaceModel;
        }

        #endregion

        #region Operations

        internal void ApplyBulkOperation()
        {
            var selectedNodes = PropertyPaletteNodes.Where(n => n.Selected);
            switch (BulkOperation)
            {
                case BulkOperation.Delete:
                    Delete(selectedNodes);
                    break;
                case BulkOperation.Freeze:
                    Freeze(selectedNodes);
                    break;
                case BulkOperation.Unfreeze:
                    Unfreeze(selectedNodes);
                    break;
                case BulkOperation.Disconnect:
                    Disconnect(selectedNodes);
                    break;
                default:
                    break;
            }
        }

        private void Disconnect(IEnumerable<PropertyPaletteNodeViewModel> selectedNodes)
        {
            foreach (var node in selectedNodes)
            {
                // Materialize the enumeration first to modify it without causing errors
                foreach (var connector in node.NodeModel.AllConnectors.ToList())
                {
                    try
                    {
                        // Invoke the Delete method which is not publicly exposed.
                        connector.GetType().GetMethod("Delete", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(connector, new object[] { });
                    }
                    catch (Exception)
                    {}
                }
            }
        }

        private void Unfreeze(IEnumerable<PropertyPaletteNodeViewModel> selectedNodes)
        {
            foreach (var node in selectedNodes)
            {
                if (node.IsFrozen)
                {
                    node.IsFrozen = false;
                }
            }
        }

        private void Freeze(IEnumerable<PropertyPaletteNodeViewModel> selectedNodes)
        {
            foreach (var node in selectedNodes)
            {
                if (!node.IsFrozen)
                {
                    node.IsFrozen = true;
                }
            }
        }

        private void Delete(IEnumerable<PropertyPaletteNodeViewModel> selectedNodes)
        {
            // Have to delete individually as multiple deletion only removes the first one
            // TODO: Investigate why the workspace's NodeRemoved even is not fired.
            foreach (var node in selectedNodes)
            {
                var command = new DynamoModel.DeleteModelCommand(node.NodeModel.GUID);
                viewLoadedParams.CommandExecutive.ExecuteCommand(command, Constants.ExtensionUniqueId, Constants.ExtensionName);
            }
        }

        #endregion

        #region Dispose or setup

        /// <summary>
        /// When switching workspaces or closing NodePropertyPalette extension,
        /// unsubscribe workspace events for profiling
        /// </summary>
        /// <param name="workspace">target workspace</param>
        private void UnsubscribeWorkspaceEvents(HomeWorkspaceModel workspace)
        {
            workspace.NodeAdded -= CurrentWorkspaceModel_NodeAdded;
            workspace.NodeRemoved -= CurrentWorkspaceModel_NodeRemoved;
            workspace.EvaluationStarted -= CurrentWorkspaceModel_EvaluationStarted;
            workspace.EvaluationCompleted -= CurrentWorkspaceModel_EvaluationCompleted;

            foreach (var node in workspace.Nodes)
            {
                node.NodeExecutionBegin -= OnNodeExecutionBegin;
                node.NodeExecutionEnd -= OnNodeExecutionEnd;
            }

            foreach (var node in PropertyPaletteNodes)
            {
                node.Dispose();
            }
            PropertyPaletteNodes.Clear();
        }

        /// <summary>
        /// When switching workspaces or closing NodePropertyPalette extension,
        /// subscribe workspace events for profiling
        /// </summary>
        /// <param name="workspace">target workspace</param>
        private void SubscribeWorkspaceEvents(HomeWorkspaceModel workspace)
        {
            workspace.NodeAdded += CurrentWorkspaceModel_NodeAdded;
            workspace.NodeRemoved += CurrentWorkspaceModel_NodeRemoved;
            workspace.EvaluationStarted += CurrentWorkspaceModel_EvaluationStarted;
            workspace.EvaluationCompleted += CurrentWorkspaceModel_EvaluationCompleted;
            
            foreach (var node in workspace.Nodes)
            {
                var profiledNode = new PropertyPaletteNodeViewModel(node);
                PropertyPaletteNodes.Add(profiledNode);
                node.NodeExecutionBegin += OnNodeExecutionBegin;
                node.NodeExecutionEnd += OnNodeExecutionEnd;
            }
            RaisePropertyChanged(nameof(PropertyPaletteNodesCollection));
        }

        /// <summary>
        /// ViewModel dispose function
        /// </summary>
        public void Dispose()
        {
            UnsubscribeWorkspaceEvents(CurrentWorkspace);
            viewLoadedParams.CurrentWorkspaceChanged -= OnCurrentWorkspaceChanged;
            viewLoadedParams.CurrentWorkspaceCleared -= OnCurrentWorkspaceCleared;
        }

        #endregion
    }
}
