/*
 * Copyright 2004,2006 The Poderosa Project.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 *
 * $Id: VT100.cs,v 1.17 2012/05/27 15:38:44 kzmi Exp $
 */
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

using Poderosa.Util.Drawing;
using Poderosa.Document;
using Poderosa.ConnectionParam;
using Poderosa.View;

namespace Poderosa.Terminal {
    internal class VT100Terminal : EscapeSequenceTerminal {

        private int _savedRow;
        private int _savedCol;
        protected bool _insertMode;
        protected bool _scrollRegionRelative;

        //接続の種類によってエスケープシーケンスの解釈を変える部分
        //protected bool _homePositionOnCSIJ2;

        public VT100Terminal(TerminalInitializeInfo info)
            : base(info) {
            _insertMode = false;
            _scrollRegionRelative = false;
            //bool sfu = _terminalSettings is SFUTerminalParam;
            //_homePositionOnCSIJ2 = sfu;
        }
        protected override void ResetInternal() {
            base.ResetInternal();
            _insertMode = false;
            _scrollRegionRelative = false;
        }


        protected override ProcessCharResult ProcessEscapeSequence(char code, char[] seq, int offset) {
            string param;
            switch (code) {
                case '[':
                    if (seq.Length - offset - 1 >= 0) {
                        param = new string(seq, offset, seq.Length - offset - 1);
                        return ProcessAfterCSI(param, seq[seq.Length - 1]);
                    }
                    break;
                //throw new UnknownEscapeSequenceException(String.Format("unknown command after CSI {0}", code));
                case ']':
                    if (seq.Length - offset - 1 >= 0) {
                        param = new string(seq, offset, seq.Length - offset - 1);
                        return ProcessAfterOSC(param, seq[seq.Length - 1]);
                    }
                    break;
                case '=':
                    ChangeMode(TerminalMode.Application);
                    return ProcessCharResult.Processed;
                case '>':
                    ChangeMode(TerminalMode.Normal);
                    return ProcessCharResult.Processed;
                case 'E':
                    ProcessNextLine();
                    return ProcessCharResult.Processed;
                case 'M':
                    ReverseIndex();
                    return ProcessCharResult.Processed;
                case 'D':
                    Index();
                    return ProcessCharResult.Processed;
                case '7':
                    SaveCursor();
                    return ProcessCharResult.Processed;
                case '8':
                    RestoreCursor();
                    return ProcessCharResult.Processed;
                case 'c':
                    FullReset();
                    return ProcessCharResult.Processed;
            }
            return ProcessCharResult.Unsupported;
        }

        protected virtual ProcessCharResult ProcessAfterCSI(string param, char code) {

            switch (code) {
                case 'c':
                    ProcessDeviceAttributes(param);
                    break;
                case 'm': //SGR
                    ProcessSGR(param);
                    break;
                case 'h':
                case 'l':
                    return ProcessDECSETMulti(param, code);
                case 'r':
                    if (param.Length > 0 && param[0] == '?')
                        return ProcessRestoreDECSET(param.Substring(1), code);
                    else
                        ProcessSetScrollingRegion(param);
                    break;
                case 's':
                    if (param.Length > 0 && param[0] == '?')
                        return ProcessSaveDECSET(param.Substring(1), code);
                    else
                        return ProcessCharResult.Unsupported;
                case 'n':
                    ProcessDeviceStatusReport(param);
                    break;
                case 'A':
                case 'B':
                case 'C':
                case 'D':
                case 'E':
                case 'F':
                    ProcessCursorMove(param, code);
                    break;
                case 'H':
                case 'f': //fは本当はxterm固有
                    ProcessCursorPosition(param);
                    break;
                case 'J':
                    ProcessEraseInDisplay(param);
                    break;
                case 'K':
                    ProcessEraseInLine(param);
                    break;
                case 'L':
                    ProcessInsertLines(param);
                    break;
                case 'M':
                    ProcessDeleteLines(param);
                    break;
                default:
                    return ProcessCharResult.Unsupported;
            }

            return ProcessCharResult.Processed;
        }
        protected virtual ProcessCharResult ProcessAfterOSC(string param, char code) {
            return ProcessCharResult.Unsupported;
        }

        protected void ProcessSGR(string param) {
            int state = 0;
            string[] ps = param.Split(';');

            foreach (string cmd in ps) {

                TextDecoration dec = _currentdecoration;
                int code = ParseSGRCode(cmd);
                if (state == 1 || state == 2) {
                    if (code == 5) {
                        state += 2;
                    }
                    else {
                        state = 0;
                    }
                }
                else if (state == 3) {
                    if (code < 256) {
                        dec = SelectForeColor(dec, code);
                    }
                    state = 0;
                }
                else if (state == 4) {
                    if (code < 256) {
                        dec = SelectBackgroundColor(dec, code);
                    }
                    state = 0;
                }
                else if (code >= 30 && code <= 37) {
                    //これだと色を変更したとき既に画面にあるものは連動しなくなるが、そこをちゃんとするのは困難である
                    dec = SelectForeColor(dec, code - 30);
                }
                else if (code >= 40 && code <= 47) {
                    dec = SelectBackgroundColor(dec, code - 40);
                }
                else if (code >= 90 && code <= 97) {
                    //これだと色を変更したとき既に画面にあるものは連動しなくなるが、そこをちゃんとするのは困難である
                    dec = SelectForeColor(dec, code - 90 + 8);
                }
                else if (code >= 100 && code <= 107) {
                    dec = SelectBackgroundColor(dec, code - 100 + 8);
                }
                else {
                    switch (code) {
                        case 0:
                            dec = TextDecoration.Default;
                            break;
                        case 1:
                        case 5:
                            dec = dec.GetCopyWithBold(true);
                            break;
                        case 4:
                            dec = dec.GetCopyWithUnderline(true);
                            break;
                        case 7:
                            dec = dec.GetInvertedCopy();
                            break;
                        case 2:
                            dec = TextDecoration.Default; //不明だがSGR 2で終わっている例があった
                            break;
                        case 22:
                        case 25:
                        case 27:
                        case 28:
                            dec = TextDecoration.Default;
                            break;
                        case 24:
                            dec = dec.GetCopyWithUnderline(false);
                            break;
                        case 38:
                            state = 1;
                            break;
                        case 39:
                            dec = dec.GetCopyWithDefaultTextColor();
                            break;
                        case 48:
                            state = 2;
                            break;
                        case 49:
                            dec = dec.GetCopyWithDefaultBackColor();
                            break;
                        case 10:
                        case 11:
                        case 12:
                            break; //'konsole'というやつらしい。無視で問題なさそう
                        default:
                            throw new UnknownEscapeSequenceException(String.Format("unknown SGR command {0}", param));
                    }
                }

                _currentdecoration = dec;
                //_manipulator.SetDecoration(dec);
            }
        }

        private TextDecoration SelectForeColor(TextDecoration dec, int index) {
            RenderProfile prof = GetRenderProfile();
            ESColor c = prof.ESColorSet[index];
            return dec.GetCopyWithTextColor(c.Color);
        }

        private TextDecoration SelectBackgroundColor(TextDecoration dec, int index) {
            RenderProfile prof = GetRenderProfile();
            ESColor c = prof.ESColorSet[index];

            Color color;
            if (prof.DarkenEsColorForBackground && !c.IsExactColor)
                color = DrawUtil.DarkColor(c.Color);
            else
                color = c.Color;

            return dec.GetCopyWithBackColor(color);
        }

        private static int ParseSGRCode(string param) {
            if (param.Length == 0)
                return 0;
            else if (param.Length == 1)
                return param[0] - '0';
            else if (param.Length == 2)
                return (param[0] - '0') * 10 + (param[1] - '0');
            else if (param.Length == 3)
                return (param[0] - '0') * 100 + (param[1] - '0') * 10 + (param[2] - '0');
            else
                throw new UnknownEscapeSequenceException(String.Format("unknown SGR parameter {0}", param));
        }

        protected virtual void ProcessDeviceAttributes(string param) {
            byte[] data = Encoding.ASCII.GetBytes(" [?1;2c"); //なんかよくわからないがMindTerm等をみるとこれでいいらしい
            data[0] = 0x1B; //ESC
            TransmitDirect(data);
        }
        protected virtual void ProcessDeviceStatusReport(string param) {
            string response;
            if (param == "5")
                response = " [0n"; //これでOKの意味らしい
            else if (param == "6")
                response = String.Format(" [{0};{1}R", GetDocument().CurrentLineNumber - GetDocument().TopLineNumber + 1, _manipulator.CaretColumn + 1);
            else
                throw new UnknownEscapeSequenceException("DSR " + param);

            byte[] data = Encoding.ASCII.GetBytes(response);
            data[0] = 0x1B; //ESC
            TransmitDirect(data);
        }

        protected void ProcessCursorMove(string param, char method) {
            int count = ParseInt(param, 1); //パラメータが省略されたときの移動量は１

            int column = _manipulator.CaretColumn;
            switch (method) {
                case 'A':
                    GetDocument().ReplaceCurrentLine(_manipulator.Export());
                    GetDocument().CurrentLineNumber = (GetDocument().CurrentLineNumber - count);
                    _manipulator.Load(GetDocument().CurrentLine, column);
                    break;
                case 'B':
                    GetDocument().ReplaceCurrentLine(_manipulator.Export());
                    GetDocument().CurrentLineNumber = (GetDocument().CurrentLineNumber + count);
                    _manipulator.Load(GetDocument().CurrentLine, column);
                    break;
                case 'C': {
                        int newvalue = column + count;
                        if (newvalue >= GetDocument().TerminalWidth)
                            newvalue = GetDocument().TerminalWidth - 1;
                        _manipulator.CaretColumn = newvalue;
                    }
                    break;
                case 'D': {
                        int newvalue = column - count;
                        if (newvalue < 0)
                            newvalue = 0;
                        _manipulator.CaretColumn = newvalue;
                    }
                    break;
            }
        }

        //CSI H
        protected void ProcessCursorPosition(string param) {
            IntPair t = ParseIntPair(param, 1, 1);
            int row = t.first, col = t.second;
            if (_scrollRegionRelative && GetDocument().ScrollingTop != -1) {
                row += GetDocument().ScrollingTop;
            }

            if (row < 1)
                row = 1;
            else if (row > GetDocument().TerminalHeight)
                row = GetDocument().TerminalHeight;
            if (col < 1)
                col = 1;
            else if (col > GetDocument().TerminalWidth)
                col = GetDocument().TerminalWidth;
            ProcessCursorPosition(row, col);
        }
        protected void ProcessCursorPosition(int row, int col) {
            GetDocument().ReplaceCurrentLine(_manipulator.Export());
            GetDocument().CurrentLineNumber = (GetDocument().TopLineNumber + row - 1);
            //int cc = GetDocument().CurrentLine.DisplayPosToCharPos(col-1);
            //Debug.Assert(cc>=0);
            _manipulator.Load(GetDocument().CurrentLine, col - 1);
        }

        //CSI J
        protected void ProcessEraseInDisplay(string param) {
            int d = ParseInt(param, 0);

            TerminalDocument doc = GetDocument();
            int cur = doc.CurrentLineNumber;
            int top = doc.TopLineNumber;
            int bottom = top + doc.TerminalHeight;
            int col = _manipulator.CaretColumn;
            switch (d) {
                case 0: //erase below
                    {
                        if (col == 0 && cur == top)
                            goto ERASE_ALL;

                        EraseRight();
                        doc.ReplaceCurrentLine(_manipulator.Export());
                        doc.EnsureLine(bottom - 1);
                        doc.RemoveAfter(bottom);
                        doc.ClearRange(cur + 1, bottom, _currentdecoration);
                        _manipulator.Load(doc.CurrentLine, col);
                    }
                    break;
                case 1: //erase above
                    {
                        if (col == doc.TerminalWidth - 1 && cur == bottom - 1)
                            goto ERASE_ALL;

                        EraseLeft();
                        doc.ReplaceCurrentLine(_manipulator.Export());
                        doc.ClearRange(top, cur, _currentdecoration);
                        _manipulator.Load(doc.CurrentLine, col);
                    }
                    break;
                case 2: //erase all
                ERASE_ALL:
                    {
                        GetDocument().ApplicationModeBackColor = (_currentdecoration != null) ? _currentdecoration.BackColor : Color.Empty;

                        doc.ReplaceCurrentLine(_manipulator.Export());
                        //if(_homePositionOnCSIJ2) { //SFUではこうなる
                        //	ProcessCursorPosition(1,1); 
                        //	col = 0;
                        //}
                        doc.EnsureLine(bottom - 1);
                        doc.RemoveAfter(bottom);
                        doc.ClearRange(top, bottom, _currentdecoration);
                        _manipulator.Load(doc.CurrentLine, col);
                    }
                    break;
                default:
                    throw new UnknownEscapeSequenceException(String.Format("unknown ED option {0}", param));
            }

        }

        //CSI K
        private void ProcessEraseInLine(string param) {
            int d = ParseInt(param, 0);

            switch (d) {
                case 0: //erase right
                    EraseRight();
                    break;
                case 1: //erase left
                    EraseLeft();
                    break;
                case 2: //erase all
                    EraseLine();
                    break;
                default:
                    throw new UnknownEscapeSequenceException(String.Format("unknown EL option {0}", param));
            }
        }

        private void EraseRight() {
            _manipulator.FillSpace(_manipulator.CaretColumn, _manipulator.BufferSize, _currentdecoration);
        }

        private void EraseLeft() {
            _manipulator.FillSpace(0, _manipulator.CaretColumn + 1, _currentdecoration);
        }

        private void EraseLine() {
            _manipulator.FillSpace(0, _manipulator.BufferSize, _currentdecoration);
        }


        protected virtual void SaveCursor() {
            _savedRow = GetDocument().CurrentLineNumber - GetDocument().TopLineNumber;
            _savedCol = _manipulator.CaretColumn;
        }
        protected virtual void RestoreCursor() {
            GLine nl = _manipulator.Export();
            GetDocument().ReplaceCurrentLine(nl);
            GetDocument().CurrentLineNumber = GetDocument().TopLineNumber + _savedRow;
            _manipulator.Load(GetDocument().CurrentLine, _savedCol);
        }

        protected void Index() {
            GLine nl = _manipulator.Export();
            GetDocument().ReplaceCurrentLine(nl);
            int current = GetDocument().CurrentLineNumber;
            if (current == GetDocument().TopLineNumber + GetDocument().TerminalHeight - 1 || current == GetDocument().ScrollingBottom)
                GetDocument().ScrollDown();
            else
                GetDocument().CurrentLineNumber = current + 1;
            _manipulator.Load(GetDocument().CurrentLine, _manipulator.CaretColumn);
        }
        protected void ReverseIndex() {
            GLine nl = _manipulator.Export();
            GetDocument().ReplaceCurrentLine(nl);
            int current = GetDocument().CurrentLineNumber;
            if (current == GetDocument().TopLineNumber || current == GetDocument().ScrollingTop)
                GetDocument().ScrollUp();
            else
                GetDocument().CurrentLineNumber = current - 1;
            _manipulator.Load(GetDocument().CurrentLine, _manipulator.CaretColumn);
        }

        protected void ProcessSetScrollingRegion(string param) {
            int height = GetDocument().TerminalHeight;
            IntPair v = ParseIntPair(param, 1, height);

            if (v.first < 1)
                v.first = 1;
            else if (v.first > height)
                v.first = height;
            if (v.second < 1)
                v.second = 1;
            else if (v.second > height)
                v.second = height;
            if (v.first > v.second) { //問答無用でエラーが良いようにも思うが
                int t = v.first;
                v.first = v.second;
                v.second = t;
            }

            //指定は1-originだが処理は0-origin
            GetDocument().SetScrollingRegion(v.first - 1, v.second - 1);
        }

        protected void ProcessNextLine() {
            GetDocument().ReplaceCurrentLine(_manipulator.Export());
            GetDocument().CurrentLineNumber = (GetDocument().CurrentLineNumber + 1);
            _manipulator.Load(GetDocument().CurrentLine, 0);
        }

        protected override void ChangeMode(TerminalMode mode) {
            if (_terminalMode == mode)
                return;

            if (mode == TerminalMode.Normal) {
                GetDocument().ClearScrollingRegion();
                GetConnection().TerminalOutput.Resize(GetDocument().TerminalWidth, GetDocument().TerminalHeight); //たとえばemacs起動中にリサイズし、シェルへ戻るとシェルは新しいサイズを認識していない
                //RMBoxで確認されたことだが、無用に後方にドキュメントを広げてくる奴がいる。カーソルを123回後方へ、など。
                //場当たり的だが、ノーマルモードに戻る際に後ろの空行を削除することで対応する。
                GLine l = GetDocument().LastLine;
                while (l != null && l.DisplayLength == 0 && l.ID > GetDocument().CurrentLineNumber)
                    l = l.PrevLine;

                if (l != null)
                    l = l.NextLine;
                if (l != null)
                    GetDocument().RemoveAfter(l.ID);

                GetDocument().IsApplicationMode = false;
            }
            else {
                GetDocument().ApplicationModeBackColor = Color.Empty;
                GetDocument().SetScrollingRegion(0, GetDocument().TerminalHeight - 1);
                GetDocument().IsApplicationMode = true;
            }

            GetDocument().InvalidateAll();

            _terminalMode = mode;
        }

        private ProcessCharResult ProcessDECSETMulti(string param, char code) {
            if (param.Length == 0)
                return ProcessCharResult.Processed;
            bool question = param[0] == '?';
            string[] ps = question ? param.Substring(1).Split(';') : param.Split(';');
            bool unsupported = false;
            foreach (string p in ps) {
                ProcessCharResult r = question ? ProcessDECSET(p, code) : ProcessSetMode(p, code);
                if (r == ProcessCharResult.Unsupported)
                    unsupported = true;
            }
            return unsupported ? ProcessCharResult.Unsupported : ProcessCharResult.Processed;
        }

        //CSI ? Pm h, CSI ? Pm l
        protected virtual ProcessCharResult ProcessDECSET(string param, char code) {
            //Debug.WriteLine(String.Format("DECSET {0} {1}", param, code));
            switch (param) {
                case "25":
                    return ProcessCharResult.Processed; //!!Show/Hide Cursorだがとりあえず無視
                case "1":
                    ChangeCursorKeyMode(code == 'h' ? TerminalMode.Application : TerminalMode.Normal);
                    return ProcessCharResult.Processed;
                default:
                    return ProcessCharResult.Unsupported;
            }
        }
        protected virtual ProcessCharResult ProcessSetMode(string param, char code) {
            bool set = code == 'h';
            switch (param) {
                case "4":
                    _insertMode = set; //hで始まってlで終わる
                    return ProcessCharResult.Processed;
                case "12":	//local echo
                    _afterExitLockActions.Add(new AfterExitLockDelegate(new LocalEchoChanger(GetTerminalSettings(), !set).Do));
                    return ProcessCharResult.Processed;
                case "20":
                    return ProcessCharResult.Processed; //!!WinXPのTelnetで確認した
                case "25":
                    return ProcessCharResult.Processed;
                case "34":	//MakeCursorBig, puttyにはある
                    //!setでカーソルを強制的に箱型にし、setで通常に戻すというのが正しい動作だが実害はないので無視
                    return ProcessCharResult.Processed;
                default:
                    return ProcessCharResult.Unsupported;
            }
        }

        //これはさぼり。ちゃんと保存しないといけない状態はほとんどないので
        protected virtual ProcessCharResult ProcessSaveDECSET(string param, char code) {
            //このparamは複数個パラメータ
            return ProcessCharResult.Processed;
        }
        protected virtual ProcessCharResult ProcessRestoreDECSET(string param, char code) {
            //このparamは複数個パラメータ
            return ProcessCharResult.Processed;
        }

        //これを送ってくるアプリケーションは viで上方スクロール
        protected void ProcessInsertLines(string param) {
            int d = ParseInt(param, 1);

            TerminalDocument doc = GetDocument();
            int caret_pos = _manipulator.CaretColumn;
            int offset = doc.CurrentLineNumber - doc.TopLineNumber;
            GLine nl = _manipulator.Export();
            doc.ReplaceCurrentLine(nl);
            if (doc.ScrollingBottom == -1)
                doc.SetScrollingRegion(0, GetDocument().TerminalHeight - 1);

            for (int i = 0; i < d; i++) {
                doc.ScrollUp(doc.CurrentLineNumber, doc.ScrollingBottom);
                doc.CurrentLineNumber = doc.TopLineNumber + offset;
            }
            _manipulator.Load(doc.CurrentLine, caret_pos);
        }

        //これを送ってくるアプリケーションは viで下方スクロール
        protected void ProcessDeleteLines(string param) {
            int d = ParseInt(param, 1);

            /*
            TerminalDocument doc = GetDocument();
            _manipulator.Clear(GetConnection().TerminalWidth);
            GLine target = doc.CurrentLine;
            for(int i=0; i<d; i++) {
                target.Clear();
                target = target.NextLine;
            }
            */

            TerminalDocument doc = GetDocument();
            int caret_col = _manipulator.CaretColumn;
            int offset = doc.CurrentLineNumber - doc.TopLineNumber;
            GLine nl = _manipulator.Export();
            doc.ReplaceCurrentLine(nl);
            if (doc.ScrollingBottom == -1)
                doc.SetScrollingRegion(0, doc.TerminalHeight - 1);

            for (int i = 0; i < d; i++) {
                doc.ScrollDown(doc.CurrentLineNumber, doc.ScrollingBottom);
                doc.CurrentLineNumber = doc.TopLineNumber + offset;
            }
            _manipulator.Load(doc.CurrentLine, caret_col);
        }



        private static string[] FUNCTIONKEY_MAP = { 
        //     F1    F2    F3    F4    F5    F6    F7    F8    F9    F10   F11  F12
              "11", "12", "13", "14", "15", "17", "18", "19", "20", "21", "23", "24",
        //     F13   F14   F15   F16   F17  F18   F19   F20   F21   F22
              "25", "26", "28", "29", "31", "32", "33", "34", "23", "24" };
        //特定のデータを流すタイプ。現在、カーソルキーとファンクションキーが該当する         
        internal override byte[] SequenceKeyData(Keys modifier, Keys body) {
            if ((int)Keys.F1 <= (int)body && (int)body <= (int)Keys.F12) {
                byte[] r = new byte[5];
                r[0] = 0x1B;
                r[1] = (byte)'[';
                int n = (int)body - (int)Keys.F1;
                if ((modifier & Keys.Shift) != Keys.None)
                    n += 10; //shiftは値を10ずらす
                char tail;
                if (n >= 20)
                    tail = (modifier & Keys.Control) != Keys.None ? '@' : '$';
                else
                    tail = (modifier & Keys.Control) != Keys.None ? '^' : '~';
                string f = FUNCTIONKEY_MAP[n];
                r[2] = (byte)f[0];
                r[3] = (byte)f[1];
                r[4] = (byte)tail;
                return r;
            }
            else if (GUtil.IsCursorKey(body)) {
                byte[] r = new byte[3];
                r[0] = 0x1B;
                if (_cursorKeyMode == TerminalMode.Normal)
                    r[1] = (byte)'[';
                else
                    r[1] = (byte)'O';

                switch (body) {
                    case Keys.Up:
                        r[2] = (byte)'A';
                        break;
                    case Keys.Down:
                        r[2] = (byte)'B';
                        break;
                    case Keys.Right:
                        r[2] = (byte)'C';
                        break;
                    case Keys.Left:
                        r[2] = (byte)'D';
                        break;
                    default:
                        throw new ArgumentException("unknown cursor key code", "key");
                }
                return r;
            }
            else {
                byte[] r = new byte[4];
                r[0] = 0x1B;
                r[1] = (byte)'[';
                r[3] = (byte)'~';
                if (body == Keys.Insert)
                    r[2] = (byte)'1';
                else if (body == Keys.Home)
                    r[2] = (byte)'2';
                else if (body == Keys.PageUp)
                    r[2] = (byte)'3';
                else if (body == Keys.Delete)
                    r[2] = (byte)'4';
                else if (body == Keys.End)
                    r[2] = (byte)'5';
                else if (body == Keys.PageDown)
                    r[2] = (byte)'6';
                else
                    throw new ArgumentException("unknown key " + body.ToString());
                return r;
            }
        }

        private class LocalEchoChanger {
            private ITerminalSettings _settings;
            private bool _value;
            public LocalEchoChanger(ITerminalSettings settings, bool value) {
                _settings = settings;
                _value = value;
            }
            public void Do() {
                _settings.BeginUpdate();
                _settings.LocalEcho = _value;
                _settings.EndUpdate();
            }
        }
    }
}
