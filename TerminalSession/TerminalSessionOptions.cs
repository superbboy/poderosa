/*
 * Copyright 2004,2006 The Poderosa Project.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 *
 * $Id: TerminalSessionOptions.cs,v 1.2 2011/10/27 23:21:59 kzmi Exp $
 */
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

using Poderosa.Preferences;
using Poderosa.Terminal;
using Poderosa.Protocols;
using Poderosa.Plugins;
using Poderosa.Serializing;
using Poderosa.Sessions;


namespace Poderosa.Sessions {
    internal class TerminalSessionOptions : SnapshotAwarePreferenceBase, ITerminalSessionOptions {
        private IBoolPreferenceItem _askCloseOnExit;
        private IIntPreferenceItem _terminalEstablishTimeout;
        private IStringPreferenceItem _telnetSSHLoginDialogUISupportTypeName;
        private IStringPreferenceItem _cygwinLoginDialogUISupportTypeName;
        private SessionDirectoryList _sessions;

        public TerminalSessionOptions(IPreferenceFolder folder)
            : base(folder) {
        }
        public override void DefineItems(IPreferenceBuilder builder) {
            _askCloseOnExit = builder.DefineBoolValue(_folder, "askCloseOnExit", false, null);

            _terminalEstablishTimeout = builder.DefineIntValue(_folder, "terminalEstablishTimeout", 5000, PreferenceValidatorUtil.PositiveIntegerValidator);
            _telnetSSHLoginDialogUISupportTypeName = builder.DefineStringValue(_folder, "telnetSSHLoginDialogUISupport", "Poderosa.Usability.MRUList", null);
            _cygwinLoginDialogUISupportTypeName = builder.DefineStringValue(_folder, "cygwinLoginDialogUISupport", "Poderosa.Usability.MRUList", null);
            _sessions = new SessionDirectoryList(TerminalSessionsPlugin.Instance.PoderosaWorld.PluginManager);
            builder.DefineLooseNode(_folder, _sessions, "sessions");
        }
        public TerminalSessionOptions Import(TerminalSessionOptions src) {
            _askCloseOnExit = ConvertItem(src._askCloseOnExit);
            _terminalEstablishTimeout = ConvertItem(src._terminalEstablishTimeout);
            return this;
        }

        public bool AskCloseOnExit {
            get {
                return _askCloseOnExit.Value;
            }
            set {
                _askCloseOnExit.Value = value;
            }
        }

        public int TerminalEstablishTimeout {
            get {
                return _terminalEstablishTimeout.Value;
            }
        }
        public string GetDefaultLoginDialogUISupportTypeName(string logintype) {
            if (_telnetSSHLoginDialogUISupportTypeName.Id == logintype)
                return _telnetSSHLoginDialogUISupportTypeName.Value;
            else if (_cygwinLoginDialogUISupportTypeName.Id == logintype)
                return _cygwinLoginDialogUISupportTypeName.Value;
            else
                return "";
        }

        public SessionDirectoryList SessionDirectoryList
        {
            get
            {
                return _sessions;
            }
        }

    }

    internal class TerminalSessionOptionsSupplier : IPreferenceSupplier {
        private IPreferenceFolder _originalFolder;
        private TerminalSessionOptions _originalOptions;

        public string PreferenceID {
            get {
                return TerminalSessionsPlugin.PLUGIN_ID; //同じとする
            }
        }

        public void InitializePreference(IPreferenceBuilder builder, IPreferenceFolder folder) {
            _originalFolder = folder;

            _originalOptions = new TerminalSessionOptions(_originalFolder);
            _originalOptions.DefineItems(builder);

            SessionDirectoryList sessionDirectoryList = _originalOptions.SessionDirectoryList;
            builder.DefineLooseNode(folder, sessionDirectoryList, "sessions");
        }

        public object QueryAdapter(IPreferenceFolder folder, Type type) {
            Debug.Assert(_originalFolder.Id == folder.Id);
            if (type == typeof(ITerminalSessionOptions))
                return folder == _originalFolder ? _originalOptions : new TerminalSessionOptions(folder).Import(_originalOptions);
            else
                return null;
        }

        public void ValidateFolder(IPreferenceFolder folder, IPreferenceValidationResult output) {
        }

        public ITerminalSessionOptions OriginalOptions {
            get {
                return _originalOptions;
            }
        }
    }

    internal class SessionItem : IAdaptable
    {
        private ITerminalParameter _terminalParam;
        private ITerminalSettings _terminalSettings;
        private StructuredText _lateBindContent; //これがnullでないときは遅延ロードの必要あり

        public SessionItem(ITerminalSession ts)
        {
            _terminalParam = ts.TerminalTransmission.Connection.Destination;
            _terminalSettings = ts.TerminalSettings;
            _lateBindContent = null;
        }
        public SessionItem(ITerminalParameter tp, ITerminalSettings ts)
        {
            _terminalParam = tp;
            _terminalSettings = ts;
            _lateBindContent = null;
        }
        public SessionItem(StructuredText latebindcontent)
        {
            _terminalParam = null;
            _terminalSettings = null;
            _lateBindContent = latebindcontent;
        }

        public ITerminalParameter TerminalParameter
        {
            get
            {
                AssureContent();
                return _terminalParam;
            }
        }
        public ITerminalSettings TerminalSettings
        {
            get
            {
                AssureContent();
                return _terminalSettings;
            }
        }
        public void IsolateSettings()
        {
            AssureContent();
            //TerminalParam, Settingsそれぞれでクローンを持つように変化させる
            _terminalParam = (ITerminalParameter)_terminalParam.Clone();
            _terminalSettings = _terminalSettings.Clone();
        }

        private void AssureContent()
        {
            if (_lateBindContent == null)
                return;

            SessionItem temp = SessionItemSerializer.Instance.Deserialize(_lateBindContent) as SessionItem;
            Debug.Assert(temp != null); //型チェックくらいはロード時にしている
            _terminalParam = temp._terminalParam;
            _terminalSettings = temp._terminalSettings;
            _lateBindContent = null; //これで遅延する
        }

        public IAdaptable GetAdapter(Type adapter)
        {
            return TerminalSessionsPlugin.Instance.PoderosaWorld.AdapterManager.GetAdapter(this, adapter);
        }
    }

    internal class SessionItemSerializer : ISerializeServiceElement
    {
        private static SessionItemSerializer _instance;
        private ISerializeService _serializeService;

        public static SessionItemSerializer Instance
        {
            get
            {
                return _instance;
            }
        }

        public SessionItemSerializer(IPluginManager pm)
        {
            _instance = this;
            _serializeService = (ISerializeService)pm.FindPlugin("org.poderosa.core.serializing", typeof(ISerializeService));
            pm.FindExtensionPoint("org.poderosa.core.serializeElement").RegisterExtension(this);
            Debug.Assert(_serializeService != null);
        }

        public Type ConcreteType
        {
            get
            {
                return typeof(SessionItem);
            }
        }


        public StructuredText Serialize(object obj)
        {
            SessionItem item = (SessionItem)obj;
            StructuredText t = new StructuredText(this.ConcreteType.FullName);
            t.AddChild(_serializeService.Serialize(item.TerminalParameter));
            t.AddChild(_serializeService.Serialize(item.TerminalSettings));
            return t;
        }

        public object Deserialize(StructuredText node)
        {
            //TODO エラーハンドリング弱い
            if (node.ChildCount != 2)
                return null;
            return new SessionItem(
                (ITerminalParameter)_serializeService.Deserialize(node.GetChildOrNull(0)),
                (ITerminalSettings)_serializeService.Deserialize(node.GetChildOrNull(1)));
        }

    }

    internal class SessionDirectory : IAdaptable
    {
        private String _name;
        private List<SessionItem> _items;
        private List<SessionDirectory> _childs;
        private StructuredText _lateBindContent;

        public SessionDirectory(String name, List<SessionItem> items, List<SessionDirectory> childs)
        {
            _name = name;
            _items = items;
            _childs = childs;
            _lateBindContent = null;
        }
        public SessionDirectory(StructuredText latebindcontent)
        {
            _name = null;
            _items = null;
            _childs = null;
            _lateBindContent = latebindcontent;
        }

        public String Name
        {
            get
            {
                AssureContent();
                return _name;
            }
        }

        public List<SessionItem> Items
        {
            get
            {
                AssureContent();
                return _items;
            }
        }

        public List<SessionDirectory> Childs
        {
            get
            {
                AssureContent();
                return _childs;
            }
        }

        public void IsolateSettings()
        {
            AssureContent();
        }

        private void AssureContent()
        {
            if (_lateBindContent == null)
                return;

            SessionDirectory temp = SessionDirectorySerializer.Instance.Deserialize(_lateBindContent) as SessionDirectory;
            Debug.Assert(temp != null); //型チェックくらいはロード時にしている
            _lateBindContent = null; //これで遅延する
        }

        public IAdaptable GetAdapter(Type adapter)
        {
            return TerminalSessionsPlugin.Instance.PoderosaWorld.AdapterManager.GetAdapter(this, adapter);
        }
    }


    internal class SessionDirectorySerializer : ISerializeServiceElement
    {
        private static SessionDirectorySerializer _instance;
        private ISerializeService _serializeService;

        public static SessionDirectorySerializer Instance
        {
            get
            {
                return _instance;
            }
        }

        public SessionDirectorySerializer(IPluginManager pm)
        {
            _instance = this;
            _serializeService = (ISerializeService)pm.FindPlugin("org.poderosa.core.serializing", typeof(ISerializeService));
            pm.FindExtensionPoint("org.poderosa.core.serializeElement").RegisterExtension(this);
            Debug.Assert(_serializeService != null);
        }

        public Type ConcreteType
        {
            get
            {
                return typeof(SessionDirectory);
            }
        }

        public StructuredText Serialize(object obj)
        {
            SessionDirectory directory = (SessionDirectory)obj;
            StructuredText t = new StructuredText(this.ConcreteType.FullName);
            t.Set("name", directory.Name);
            if (null != directory.Items)
            {
                StructuredText items = new StructuredText("items");
                foreach (SessionItem tp in directory.Items)
                {
                    try
                    {
                        items.AddChild(_serializeService.Serialize(tp));
                    }
                    catch (Exception ex)
                    {
                        RuntimeUtil.ReportException(ex);
                    }
                }
                t.AddChild(items);
            }
            if (null != directory.Childs)
            {
                StructuredText childs = new StructuredText("childs");
                foreach (SessionDirectory tp in directory.Childs)
                {
                    try
                    {
                        childs.AddChild(_serializeService.Serialize(tp));
                    }
                    catch (Exception ex)
                    {
                        RuntimeUtil.ReportException(ex);
                    }
                }
                t.AddChild(childs);
            }
            
            return t;
        }

        public object Deserialize(StructuredText node)
        {
            String name = node.Get("name");
            List<SessionItem> items = new List<SessionItem>();
            foreach (StructuredText item in node.FindChild("items").Children) {
                try {                    
                    items.Add(new SessionItem(item));
                }
                catch (Exception ex) {
                    RuntimeUtil.ReportException(ex);
                }
            }
            List<SessionDirectory> childs = new List<SessionDirectory>();
            foreach (StructuredText item in node.FindChild("childs").Children) {
                try {                    
                    childs.Add(new SessionDirectory(item));
                }
                catch (Exception ex) {
                    RuntimeUtil.ReportException(ex);
                }
            }
            return new SessionDirectory(name, items, childs);
        }

    }

    internal class SessionDirectoryList : IPreferenceLooseNodeContent
    {
        private SessionDirectorySerializer _serializer;
        private List<SessionDirectory> _childs;
        public SessionDirectoryList(IPluginManager pm)
        {
            _serializer = new SessionDirectorySerializer(pm);            
            _childs = new List<SessionDirectory>();
        }

        public void Reset()
        {
            _childs.Clear();
        }
        public IPreferenceLooseNodeContent Clone()
        {
            return this; //TODO さぼり。今はコピーに対して編集するようなことがないのでこれでも構わないが
        }

        public void LoadFrom(StructuredText node)
        {
            _childs.Clear();
            foreach (StructuredText item in node.Children)
            {
                try
                {
                    _childs.Add(new SessionDirectory(item));
                }
                catch (Exception ex)
                {
                    RuntimeUtil.ReportException(ex);
                }
            }
        }

        public void SaveTo(StructuredText node)
        {
            foreach (SessionDirectory tp in _childs)
            {
                try
                {
                    node.AddChild(_serializer.Serialize(tp));
                }
                catch (Exception ex)
                {
                    RuntimeUtil.ReportException(ex);
                }
            }
        }
    }


}
