using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;


namespace PadAnalyzer
{
    public partial class PadAnalyzer : Form
    {
        public PadAnalyzer()
        {
            InitializeComponent();
            m_table = CreateDataTable();
            bindingSourceSymbols.DataSource = m_table;
            dataGridSymbols.DataSource = bindingSourceSymbols;

            dataGridSymbols.Columns[0].Width = 271;
            progressBar.Maximum = progressBar.Width;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void loadPDBToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (openPdbDialog.ShowDialog() == DialogResult.OK)
            {
                progressBar.Value = 0;
                bgWorker.RunWorkerAsync(openPdbDialog.FileName);
            }
        }

        DataTable CreateDataTable()
        {
            DataTable table = new DataTable("Symbols");

            DataColumn column = new DataColumn();
            column.ColumnName = "Symbol";
            column.ReadOnly = true;
            table.Columns.Add(column);
            column = new DataColumn();
            column.ColumnName = "Size";
            column.ReadOnly = true;
            column.DataType = System.Type.GetType("System.Int32");
            table.Columns.Add(column);
            column = new DataColumn();
            column.ColumnName = "Padding";
            column.ReadOnly = true;
            column.DataType = System.Type.GetType("System.Int32");
            table.Columns.Add(column);
            column = new DataColumn();
            column.ColumnName = "Padding/Size";
            column.ReadOnly = true;
            column.DataType = System.Type.GetType("System.Double");
            table.Columns.Add(column);

            return table;
        }

        CsDebugScript.Engine.ISymbolProviderModule sym;

        void PopulateDataTable(string fileName)
        {
            m_symbols.Clear();

            if (fileName.ToLower().EndsWith(".pdb"))
            {
                CsDebugScript.Engine.SymbolProviders.DiaSymbolProvider dw = new CsDebugScript.Engine.SymbolProviders.DiaSymbolProvider();
                sym = dw.LoadModule(fileName);
            }
            else
            {
                CsDebugScript.DwarfSymbolProvider.DwarfSymbolProvider dw = new CsDebugScript.DwarfSymbolProvider.DwarfSymbolProvider();
                sym = dw.LoadModule(fileName);
            }

            try
            {
            }
            catch (System.Runtime.InteropServices.COMException exc)
            {
                MessageBox.Show(this, exc.ToString());
                return;
            }

            foreach (uint typeId in sym.GetAllTypes())
            {
                TryAddSymbol(typeId);
            }

            /*
            IDiaEnumSymbols allSymbols;
            m_session.findChildren(m_session.globalScope, SymTagEnum.SymTagUDT, null, 0, out allSymbols);

            ulong cacheLineSize = GetCacheLineSize();

            nSymbols = allSymbols.Count;
            symIdx = 0;
            symStep = nSymbols / progressBar.Maximum;

            foreach (IDiaSymbol sym in allSymbols)
            {
                symIdx++;

                if (symIdx % symStep == 0)
                {
                    bgWorker.ReportProgress(symIdx * progressBar.Maximum / nSymbols);
                }

                TryAddSymbol(sym);
            }
            */
        }

        int nSymbols;
        int symIdx;
        int symStep;

        SymbolInfo TryAddSymbol(uint typeId)
        {
            string name = sym.GetTypeName(typeId);

            uint size = sym.GetTypeSize(typeId);
            if (size > 0 && name != null)
            {
                if (!m_symbols.ContainsKey(name))
                {
                    SymbolInfo info = new SymbolInfo();
                    info.Set(name, (int)size, 0, "");
                    m_symbols.Add(info.m_name, info);

                    ProcessChildren(info, typeId);
                    return info;
                }
                else
                {
                    return m_symbols[name];
                }
            }

            return null;
        }

        ulong GetCacheLineSize()
        {
            return Convert.ToUInt32(textBoxCache.Text);
        }

        void ProcessChildren(SymbolInfo info, uint typeId)
        {
            foreach (string fieldName in sym.GetTypeFieldNames(typeId))
            {
                Tuple<uint, int> fieldTypeAndOffset = sym.GetTypeFieldTypeAndOffset(typeId, fieldName);

                SymbolInfo childInfo;
                if (ProcessChild(fieldName, fieldTypeAndOffset.Item1, fieldTypeAndOffset.Item2, out childInfo))
                    info.AddChild(childInfo);
            }

            foreach (Tuple<uint, int> baseClass in sym.GetTypeDirectBaseClasses(typeId).Values)
            {
                SymbolInfo childInfo;
                if (ProcessBase(baseClass.Item1, baseClass.Item2, out childInfo))
                    info.AddChild(childInfo);
            }


            // Sort children by offset, recalc padding.
            // Sorting is not needed normally (for data fields), but sometimes base class order is wrong.
            if (info.HasChildren())
            {
                info.m_children.Sort(SymbolInfo.CompareOffsets);
                for (int i = 0; i < info.m_children.Count; ++i)
                {
                    SymbolInfo child = info.m_children[i];
                    int next_ofs;

                    if (i < info.m_children.Count - 1)
                    {
                        next_ofs = info.m_children[i + 1].m_offset;
                    }
                    else
                    {
                        next_ofs = info.m_size;
                    }

                    int pad = (next_ofs - (int)child.m_offset) - child.m_size;
                    if (pad < 0)
                    {
                        pad = 0;
                    }
                    child.m_padding += pad;
                    info.m_padding += child.m_padding;
                }
            }
        }

        readonly string[] baseTypes = { "none", "void", "char", "wchar", "4", "5", "int", "uint", "float", "BCD", "bool", "11", "12", "long", "ulong" };


        bool ProcessBase(uint typeId, int memOffset, out SymbolInfo info)
        {
            string typeName = sym.GetTypeName(typeId);
            ulong typeSize = sym.GetTypeSize(typeId);

            info = new SymbolInfo();

            SymbolInfo typeInfo = null;
            int count = 1;

            typeInfo = TryAddSymbol(typeId);

            if (typeInfo != null)
            {
                info.m_padding += typeInfo.m_padding * count;
            }

            info.Set("Base: " + typeName, (int)typeSize, memOffset, typeName);

            return true;
        }

        bool ProcessChild(string fieldName, uint typeId, int memOffset, out SymbolInfo info)
        {
            string symbolName = sym.GetTypeName(typeId);

            info = new SymbolInfo();

            ulong len = sym.GetTypeSize(typeId);
            if (symbolName != null)
            {

                SymbolInfo typeInfo = null;
                int count = 1;

                typeInfo = TryAddSymbol(typeId);

                if (typeInfo != null)
                {
                    info.m_padding += typeInfo.m_padding * count;
                }
            }

            info.Set(fieldName, (int)len, memOffset, symbolName);

            return true;
        }

        class SymbolInfo
        {
            public void Set(string name, int size, int offset, string type_name)
            {
                m_name = name;
                m_type_name = type_name;
                m_size = size;
                m_offset = offset;
            }

            public bool HasChildren() { return m_children != null; }
            public void AddChild(SymbolInfo child)
            {
                if (m_children == null)
                    m_children = new List<SymbolInfo>();
                m_children.Add(child);
            }
            public bool IsBase()
            {
                return m_name.IndexOf("Base: ") == 0;
            }

            public static int CompareOffsets(SymbolInfo x, SymbolInfo y)
            {
                // Base classes have to go first.
                if (x.IsBase() && !y.IsBase())
                    return -1;
                if (!x.IsBase() && y.IsBase())
                    return 1;

                return (x.m_offset == y.m_offset) ? 0 :
                    (x.m_offset < y.m_offset) ? -1 : 1;
            }

            public string m_name;
            public string m_type_name;
            public int m_size;
            public int m_offset;
            public int m_padding = 0;
            public List<SymbolInfo> m_children;
        };

        SymbolInfo FindSelectedSymbolInfo()
        {
            if (dataGridSymbols.SelectedRows.Count == 0)
                return null;

            DataGridViewRow selectedRow = dataGridSymbols.SelectedRows[0];
            string symbolName = selectedRow.Cells[0].Value.ToString();

            SymbolInfo info = FindSymbolInfo(symbolName);
            return info;
        }
        SymbolInfo FindSymbolInfo(string name)
        {
            SymbolInfo info;
            m_symbols.TryGetValue(name, out info);
            return info;
        }

        Dictionary<string, SymbolInfo> m_symbols = new Dictionary<string, SymbolInfo>();
        DataTable m_table = null;
        long m_prefetchStartOffset = 0;

        private void dataGridSymbols_SelectionChanged(object sender, EventArgs e)
        {
            m_prefetchStartOffset = 0;
            ShowSelectedSymbolInfo();
        }

        void ShowSelectedSymbolInfo()
        {
            dataGridViewSymbolInfo.Rows.Clear();
            SymbolInfo info = FindSelectedSymbolInfo();
            if (info != null)
                ShowSymbolInfo(info);
        }

        void ShowSymbolInfo(SymbolInfo info)
        {
            dataGridViewSymbolInfo.Rows.Clear();
            if (!info.HasChildren())
                return;

            long cacheLineSize = (long)GetCacheLineSize();
            long prevCacheBoundaryOffset = m_prefetchStartOffset;

            if (prevCacheBoundaryOffset > (long)info.m_size)
                prevCacheBoundaryOffset = (long)info.m_size;

            long numCacheLines = 0;
            foreach (SymbolInfo child in info.m_children)
            {
                if (cacheLineSize > 0)
                {
                    while (child.m_offset - prevCacheBoundaryOffset >= cacheLineSize)
                    {
                        numCacheLines = numCacheLines + 1;
                        long cacheLineOffset = numCacheLines * cacheLineSize + m_prefetchStartOffset;
                        string[] boundaryRow = { "Cacheline boundary", cacheLineOffset.ToString(), "", "" };
                        dataGridViewSymbolInfo.Rows.Add(boundaryRow);
                        prevCacheBoundaryOffset = cacheLineOffset;
                    }
                }

                string[] row = { child.m_name, child.m_offset.ToString(), child.m_size.ToString(), child.m_type_name };
                dataGridViewSymbolInfo.Rows.Add(row);

                if (child.m_padding > 0)
                {
                    long paddingOffset = child.m_offset + child.m_size;
                    string[] paddingRow = { "Padding", paddingOffset.ToString(), child.m_padding.ToString(), "" };
                    dataGridViewSymbolInfo.Rows.Add(paddingRow);
                }
            }
        }

        private void dataGridSymbols_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (e.Column.Index > 0)
            {
                e.Handled = true;
                e.SortResult = Int32.Parse(e.CellValue1.ToString()) - Int32.Parse(e.CellValue2.ToString());
            }
        }

        private void dataGridViewSymbolInfo_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            foreach (DataGridViewRow row in dataGridViewSymbolInfo.Rows)
            {
                DataGridViewCell cell = row.Cells[0];
                if (cell.Value.ToString().StartsWith("Padding"))
                {
                    cell.Style.BackColor = Color.LightGray;
                    row.Cells[1].Style.BackColor = Color.LightGray;
                    row.Cells[2].Style.BackColor = Color.LightGray;
                }
                else if (cell.Value.ToString().IndexOf("Base: ") == 0)
                {
                    cell.Style.BackColor = Color.LightGreen;
                    row.Cells[1].Style.BackColor = Color.LightGreen;
                    row.Cells[2].Style.BackColor = Color.LightGreen;
                }
                else if (cell.Value.ToString() == "Cacheline boundary")
                {
                    cell.Style.BackColor = Color.LightPink;
                    row.Cells[1].Style.BackColor = Color.LightPink;
                    row.Cells[2].Style.BackColor = Color.LightPink;
                }
            }
        }

        private void textBoxFilter_TextChanged(object sender, EventArgs e)
        {
            if (textBoxFilter.Text.Length == 0)
                bindingSourceSymbols.Filter = null;
            else
                bindingSourceSymbols.Filter = "Symbol LIKE '*" + textBoxFilter.Text + "*'";
        }

        void dumpSymbolInfo(System.IO.TextWriter tw, SymbolInfo info)
        {
            tw.WriteLine("Symbol: " + info.m_name);
            tw.WriteLine("Size: " + info.m_size.ToString());
            tw.WriteLine("Total padding: " + info.m_padding.ToString());
            tw.WriteLine("Members");
            tw.WriteLine("-------");

            foreach (SymbolInfo child in info.m_children)
            {
                if (child.m_padding > 0)
                {
                    long paddingOffset = child.m_offset - child.m_padding;
                    tw.WriteLine(String.Format("{0,-40} {1,5} {2,5}", "****Padding", paddingOffset, child.m_padding));
                }

                tw.WriteLine(String.Format("{0,-40} {1,5} {2,5}", child.m_name, child.m_offset, child.m_size));
            }
            // Final structure padding.
            if (info.m_padding > 0)
            {
                long paddingOffset = (long)info.m_size - info.m_padding;
                tw.WriteLine(String.Format("{0,-40} {1,5} {2,5}", "****Padding", paddingOffset, info.m_padding));
            }
        }

        private void copyTypeLayoutToClipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SymbolInfo info = FindSelectedSymbolInfo();
            if (info != null)
            {
                System.IO.StringWriter tw = new System.IO.StringWriter();
                dumpSymbolInfo(tw, info);
                Clipboard.SetText(tw.ToString());
            }
        }

        private void textBoxCache_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Enter || e.KeyChar == (char)Keys.Escape)
                ShowSelectedSymbolInfo();
            base.OnKeyPress(e);
        }

        private void textBoxCache_Leave(object sender, EventArgs e)
        {
            ShowSelectedSymbolInfo();
        }

        private void setPrefetchStartOffsetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (dataGridViewSymbolInfo.SelectedRows.Count != 0)
            {
                DataGridViewRow selectedRow = dataGridViewSymbolInfo.SelectedRows[0];
                long symbolOffset = Convert.ToUInt32(selectedRow.Cells[1].Value);
                m_prefetchStartOffset = symbolOffset;
                ShowSelectedSymbolInfo();
            }
        }

        private void bgWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar.Value = e.ProgressPercentage;
        }

        private void bgWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            PopulateDataTable(e.Argument as string);
        }

        private void bgWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            m_table.Rows.Clear();
            progressBar.Value = progressBar.Maximum;

            m_table.BeginLoadData();
            foreach (SymbolInfo info in m_symbols.Values)
            {
                long totalPadding = info.m_padding;

                DataRow row = m_table.NewRow();
                row["Symbol"] = info.m_name;
                row["Size"] = info.m_size;
                row["Padding"] = totalPadding;
                row["Padding/Size"] = (double)totalPadding / info.m_size;
                m_table.Rows.Add(row);
            }
            m_table.EndLoadData();

            //
            // Sort by name by default (ascending)
            dataGridSymbols.Sort(dataGridSymbols.Columns[0], ListSortDirection.Ascending);
            bindingSourceSymbols.Filter = null;// "Symbol LIKE '*rde*'";

            ShowSelectedSymbolInfo();
        }
    }
}
