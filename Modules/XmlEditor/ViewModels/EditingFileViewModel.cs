﻿using Common;

using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

using XmlEditor.Message;

namespace XmlEditor.ViewModels
{
    public class EditingFileViewModel : BindableBase
    {
        private readonly IEventAggregator _eventAggregator;

        public EditingFileViewModel(IEventAggregator ea)
        {
            _eventAggregator = ea;
        }

        #region Property

        private XElement Root { get; set; }

        private string FileType { get; set; }

        private int EditingNodeIndex { get; set; }

        #endregion

        #region Dependency Property

        private string _Name;
        public string Name
        {
            get { return _Name; }
            set { SetProperty(ref _Name, value); }
        }

        private string _Uri;
        public string Uri
        {
            get { return _Uri; }
            set
            {
                SetProperty(ref _Uri, value);
                Load(_Uri);
            }
        }

        private bool _IsEditing;
        public bool IsEditing
        {
            get { return _IsEditing; }
            set { SetProperty(ref _IsEditing, value); }
        }

        private int _NodeSelectedIndex;
        public int NodeSelectedIndex
        {
            get { return _NodeSelectedIndex; }
            set { SetProperty(ref _NodeSelectedIndex, value); }
        }

        private ObservableCollection<XmlNodeViewModel> _XmlNodes;
        public ObservableCollection<XmlNodeViewModel> XmlNodes
        {
            get { return _XmlNodes; }
            set { SetProperty(ref _XmlNodes, value); }
        }

        private ObservableCollection<AttributeViewModel> _EditingXmlAttributes;
        public ObservableCollection<AttributeViewModel> EditingXmlAttributes
        {
            get { return _EditingXmlAttributes; }
            set { SetProperty(ref _EditingXmlAttributes, value); }
        }

        #endregion

        #region Command

        private DelegateCommand<XmlNodeViewModel> _NodeLeftDoubleClickCommand;
        public DelegateCommand<XmlNodeViewModel> NodeLeftDoubleClickCommand =>
            _NodeLeftDoubleClickCommand ??= new DelegateCommand<XmlNodeViewModel>(ExecuteNodeLeftDoubleClickCommand);

        void ExecuteNodeLeftDoubleClickCommand(XmlNodeViewModel parameter)
        {
            if (parameter.UnUse)
            {
                return;
            }

            EditingNodeIndex = NodeSelectedIndex;

            EditingXmlAttributes = parameter.XmlAttributes;
        }

        private DelegateCommand<string> _SearchCommand;
        public DelegateCommand<string> SearchCommand =>
            _SearchCommand ?? (_SearchCommand = new DelegateCommand<string>(ExecuteSearchCommand));

        void ExecuteSearchCommand(string parameter)
        {
            for (int i = NodeSelectedIndex + 1; i < XmlNodes.Count; i++)
            {
                if (XmlNodes[i].UnUse)
                {
                    continue;
                }

                if (!XmlNodes[i].Title.Contains(parameter) && null != XmlNodes[i].Desc && !XmlNodes[i].Desc.Contains(parameter))
                {
                    continue;
                }
                NodeSelectedIndex = i;
                return;
            }

            for (int i = 0; i < NodeSelectedIndex; i++)
            {
                if (XmlNodes[i].UnUse)
                {
                    continue;
                }

                if (!XmlNodes[i].Title.Contains(parameter) && !XmlNodes[i].Desc.Contains(parameter))
                {
                    continue;
                }
                NodeSelectedIndex = i;
                return;
            }
        }

        private DelegateCommand<DataGridCellEditEndingEventArgs> _CellEditEndingCommand;
        public DelegateCommand<DataGridCellEditEndingEventArgs> CellEditEndingCommand =>
            _CellEditEndingCommand ??= new DelegateCommand<DataGridCellEditEndingEventArgs>(ExecuteCellEditEndingCommand);

        void ExecuteCellEditEndingCommand(DataGridCellEditEndingEventArgs parameter)
        {
            if (parameter.EditAction != DataGridEditAction.Commit)
            {
                return;
            }

            // 排除desc
            if (parameter.Column.DisplayIndex == 1 || parameter.Column.DisplayIndex == 3)
            {
                return;
            }

            var originRow = (AttributeViewModel)parameter.Row.Item;
            var header = parameter.Column.Header.ToString();
            var input = ((TextBox)parameter.EditingElement).Text;

            if (header == "Value" && !string.Equals(originRow.Value, input, StringComparison.Ordinal))
            {
                // update by Attr
                var attrM = Desc.FindAttrAndValueDesc(originRow.Attr, input, FileType);
                originRow.Attr = attrM.Attr;
                originRow.AttrDesc = attrM.AttrDesc;
                originRow.Value = attrM.Value;
                originRow.ValueDesc = attrM.ValueDesc;

                if (attrM.Attr == Desc.FindTitleAttr(FileType))
                {
                    XmlNodes[EditingNodeIndex].Title = attrM.Value;
                    XmlNodes[EditingNodeIndex].Desc = attrM.ValueDesc;
                }

                IsEditing = true;
                return;
            }
            if (string.Equals(header, "Attr", StringComparison.Ordinal) && !string.Equals(originRow.Attr, input, StringComparison.Ordinal))
            {
                // update by Value
                var attrM = Desc.FindAttrAndValueDesc(input, originRow.Value, FileType);
                originRow.Attr = attrM.Attr;
                originRow.AttrDesc = attrM.AttrDesc;
                originRow.Value = attrM.Value;
                originRow.ValueDesc = attrM.ValueDesc;

                IsEditing = true;
                return;
            }
        }

        #endregion

        #region Method

        private void Load(string uri)
        {
            XDocument xDocument = XDocument.Load(uri);
            FileType = xDocument.Root.Attribute("type").Value;
            string titleAttr = Desc.FileSchemeDescs.GetValueOrDefault(FileType, null)?.TitleAttr;

            Root = new XElement(xDocument.Root.Name);
            foreach (var attr in xDocument.Root.Attributes())
            {
                Root.Add(attr);
            }

            XmlNodes = new ObservableCollection<XmlNodeViewModel>(xDocument.Root.Nodes()
                .Select(node =>
                {
                    var xmlNode = new XmlNodeViewModel();

                    // 不是节点需要保留写回源文件
                    if (node.NodeType != XmlNodeType.Element)
                    {
                        xmlNode.UnUse = true;
                        xmlNode.Node = node;
                        xmlNode.Title = node.ToString();
                        xmlNode.Desc = node.NodeType.ToString();
                        return xmlNode;
                    }

                    XElement element = node as XElement;

                    string title = element.Attribute(titleAttr)?.Value ??
                        element.Attribute("alias")?.Value ??
                        element.Attribute("dayofweek")?.Value ??
                        element.Attribute("store2")?.Value ??
                        element.Attribute("job")?.Value;

                    List<AttributeViewModel> attrList = new();
                    foreach (var attr in element.Attributes())
                    {
                        var attrM = Desc.FindAttrAndValueDesc(attr.Name.LocalName, attr.Value, FileType);

                        attrList.Add(new AttributeViewModel()
                        {
                            Attr = attrM.Attr,
                            AttrDesc = attrM.AttrDesc,
                            Value = attrM.Value,
                            ValueDesc = attrM.ValueDesc
                        });
                    }

                    xmlNode.Title = title;
                    xmlNode.XmlAttributes = new(attrList);
                    xmlNode.Desc = xmlNode.XmlAttributes.FirstOrDefault(ss => ss.Value == title)?.ValueDesc;

                    return xmlNode;
                }));
        }

        public void Save()
        {
            XDocument xDocument = new XDocument();
            xDocument.Add(new XElement(Root));

            foreach (var node in XmlNodes)
            {
                if (node.UnUse)
                {
                    xDocument.Root.Add(node.Node);
                    continue;
                }

                var record = new XElement("record");

                var attrs = node.XmlAttributes.Select(attr => new XAttribute(XName.Get(attr.Attr), attr.Value));
                foreach (var attr in attrs)
                {
                    record.Add(attr);
                }

                xDocument.Root.Add(record);
            }
            xDocument.Save(Uri);

            IsEditing = false;
        }

        #endregion
    }
}
