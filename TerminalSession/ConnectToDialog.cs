/*
 * Copyright 2004,2006 The Poderosa Project.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 *
 * $Id: ConnectToDialog.cs,v 1.0 2014/10/21 superbboy $
 */
using System;
using System.Drawing;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Forms;
using System.Diagnostics;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;

using Poderosa.Plugins;
using Poderosa.Util;
using Poderosa.Terminal;
using Poderosa.ConnectionParam;
using Poderosa.Protocols;
using Poderosa.Forms;
using Poderosa.View;
using Poderosa.UI;

using Granados;


namespace Poderosa.Sessions {
    internal class ConnectToDialog : LoginDialogBase {

        private const int TELNET_PORT = 23;
        private const int SSH_PORT = 22;

        private ITerminalParameter _param;

        private bool _initializing;
        private bool _firstFlag;
        private TreeView hostTree;
        private Button newDirectory;
        private Button deleteItem;
        private Button modifyItem;
        private Button newItem;
        //private ConnectionHistory _history;

        private System.ComponentModel.Container components = null;

        public ConnectToDialog(IPoderosaMainWindow parentWindow)
            : base(parentWindow) {
            _firstFlag = true;
            _initializing = true;
            //_history = GApp.ConnectionHistory;
            //
            // Windows フォーム デザイナ サポートに必要です。
            //
            InitializeComponent();            
            _initializing = false;
        }
        /// <summary>
        /// 使用されているリソースに後処理を実行します。
        /// </summary>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (components != null) {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code
        /// <summary>
        /// デザイナ サポートに必要なメソッドです。このメソッドの内容を
        /// コード エディタで変更しないでください。
        /// </summary>
        private void InitializeComponent() {
            this.hostTree = new System.Windows.Forms.TreeView();
            this.newDirectory = new System.Windows.Forms.Button();
            this.deleteItem = new System.Windows.Forms.Button();
            this.modifyItem = new System.Windows.Forms.Button();
            this.newItem = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // _loginButton
            // 
            this._loginButton.ImageIndex = 0;
            this._loginButton.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this._loginButton.Location = new System.Drawing.Point(282, 459);
            this._loginButton.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this._loginButton.Size = new System.Drawing.Size(72, 27);
            this._loginButton.TabIndex = 11;
            // 
            // _cancelButton
            // 
            this._cancelButton.ImageIndex = 0;
            this._cancelButton.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this._cancelButton.Location = new System.Drawing.Point(370, 459);
            this._cancelButton.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this._cancelButton.Size = new System.Drawing.Size(72, 27);
            this._cancelButton.TabIndex = 12;
            // 
            // hostTree
            // 
            this.hostTree.Location = new System.Drawing.Point(14, 40);
            this.hostTree.Name = "hostTree";
            this.hostTree.Size = new System.Drawing.Size(433, 405);
            this.hostTree.TabIndex = 13;
            // 
            // newDirectory
            // 
            this.newDirectory.Location = new System.Drawing.Point(13, 12);
            this.newDirectory.Name = "newDirectory";
            this.newDirectory.Size = new System.Drawing.Size(84, 23);
            this.newDirectory.TabIndex = 14;
            this.newDirectory.Text = "NewDirectory";
            this.newDirectory.UseVisualStyleBackColor = true;
            // 
            // deleteItem
            // 
            this.deleteItem.Location = new System.Drawing.Point(254, 12);
            this.deleteItem.Name = "deleteItem";
            this.deleteItem.Size = new System.Drawing.Size(75, 23);
            this.deleteItem.TabIndex = 15;
            this.deleteItem.Text = "Delete";
            this.deleteItem.UseVisualStyleBackColor = true;
            // 
            // modifyItem
            // 
            this.modifyItem.Location = new System.Drawing.Point(370, 12);
            this.modifyItem.Name = "modifyItem";
            this.modifyItem.Size = new System.Drawing.Size(75, 23);
            this.modifyItem.TabIndex = 16;
            this.modifyItem.Text = "Modify";
            this.modifyItem.UseVisualStyleBackColor = true;
            // 
            // newItem
            // 
            this.newItem.Location = new System.Drawing.Point(138, 11);
            this.newItem.Name = "newItem";
            this.newItem.Size = new System.Drawing.Size(75, 23);
            this.newItem.TabIndex = 17;
            this.newItem.Text = "NewItem";
            this.newItem.UseVisualStyleBackColor = true;
            // 
            // ConnectToDialog
            // 
            this.AcceptButton = this._loginButton;
            this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
            this.CancelButton = this._cancelButton;
            this.ClientSize = new System.Drawing.Size(458, 457);
            this.Controls.Add(this.newItem);
            this.Controls.Add(this.modifyItem);
            this.Controls.Add(this.deleteItem);
            this.Controls.Add(this.newDirectory);
            this.Controls.Add(this.hostTree);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ConnectToDialog";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Controls.SetChildIndex(this._loginButton, 0);
            this.Controls.SetChildIndex(this._cancelButton, 0);
            this.Controls.SetChildIndex(this.hostTree, 0);
            this.Controls.SetChildIndex(this.newDirectory, 0);
            this.Controls.SetChildIndex(this.deleteItem, 0);
            this.Controls.SetChildIndex(this.modifyItem, 0);
            this.Controls.SetChildIndex(this.newItem, 0);
            this.ResumeLayout(false);

        }
        #endregion

        protected override void StartConnection()
        {
            ISSHLoginParameter ssh = (ISSHLoginParameter)_param.GetAdapter(typeof(ISSHLoginParameter));
            ITCPParameter tcp = (ITCPParameter)_param.GetAdapter(typeof(ITCPParameter));
            IProtocolService protocolservice = TerminalSessionsPlugin.Instance.ProtocolService;
            if (ssh != null)
                _connector = protocolservice.AsyncSSHConnect(this, ssh);
            else
                _connector = protocolservice.AsyncTelnetConnect(this, tcp);

            if (_connector == null)
                ClearConnectingState();
        }
        protected override ITerminalParameter PrepareTerminalParameter()
        {
            return null;
        }
        protected override void ShowError(string msg)
        {
            GUtil.Warning(this, msg, TEnv.Strings.GetString("Caption.LoginDialog.ConnectionError"));
        }
        public void ApplyParam()
        {
        }

    }
}
