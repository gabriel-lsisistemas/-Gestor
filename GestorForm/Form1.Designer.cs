namespace LsiGestor
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            textBoxResponse = new TextBox();
            contextMenuStrip1 = new ContextMenuStrip(components);
            sairToolStripMenuItem = new ToolStripMenuItem();
            notifyIcon1 = new NotifyIcon(components);
            groupBoxActions = new GroupBox();
            buttonParcelas = new Button();
            buttonCancelLoop = new Button();
            buttonStartLoop = new Button();
            buttonSendAll = new Button();
            button1EnvioInicial = new Button();
            contextMenuStrip1.SuspendLayout();
            groupBoxActions.SuspendLayout();
            SuspendLayout();
            // 
            // textBoxResponse
            // 
            textBoxResponse.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBoxResponse.BackColor = SystemColors.Window;
            textBoxResponse.ForeColor = SystemColors.WindowText;
            textBoxResponse.Location = new Point(10, 76);
            textBoxResponse.Multiline = true;
            textBoxResponse.Name = "textBoxResponse";
            textBoxResponse.Size = new Size(658, 357);
            textBoxResponse.TabIndex = 3;
            textBoxResponse.TextChanged += textBoxResponse_TextChanged;
            // 
            // contextMenuStrip1
            // 
            contextMenuStrip1.Items.AddRange(new ToolStripItem[] { sairToolStripMenuItem });
            contextMenuStrip1.Name = "contextMenuStrip1";
            contextMenuStrip1.Size = new Size(94, 26);
            // 
            // sairToolStripMenuItem
            // 
            sairToolStripMenuItem.Name = "sairToolStripMenuItem";
            sairToolStripMenuItem.Size = new Size(93, 22);
            sairToolStripMenuItem.Text = "Sair";
            sairToolStripMenuItem.Click += sairToolStripMenuItem_Click;
            // 
            // notifyIcon1
            // 
            notifyIcon1.BalloonTipIcon = ToolTipIcon.Info;
            notifyIcon1.BalloonTipText = "Seu aplicativo está funcionando em segundo plano.";
            notifyIcon1.BalloonTipTitle = "LSI Gestor";
            notifyIcon1.ContextMenuStrip = contextMenuStrip1;
            notifyIcon1.Icon = (Icon)resources.GetObject("notifyIcon1.Icon");
            notifyIcon1.Text = "LSI Gestor";
            notifyIcon1.Visible = true;
            notifyIcon1.MouseDoubleClick += notifyIcon1_MouseDoubleClick;
            // 
            // groupBoxActions
            // 
            groupBoxActions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            groupBoxActions.Controls.Add(buttonParcelas);
            groupBoxActions.Controls.Add(buttonCancelLoop);
            groupBoxActions.Controls.Add(buttonStartLoop);
            groupBoxActions.Controls.Add(buttonSendAll);
            groupBoxActions.Controls.Add(button1EnvioInicial);
            groupBoxActions.Location = new Point(13, 12);
            groupBoxActions.Name = "groupBoxActions";
            groupBoxActions.Size = new Size(655, 61);
            groupBoxActions.TabIndex = 6;
            groupBoxActions.TabStop = false;
            groupBoxActions.Text = "Ações";
            groupBoxActions.Enter += groupBoxActions_Enter;
            // 
            // buttonParcelas
            // 
            buttonParcelas.Location = new Point(510, 21);
            buttonParcelas.Name = "buttonParcelas";
            buttonParcelas.Size = new Size(120, 30);
            buttonParcelas.TabIndex = 0;
            buttonParcelas.Text = "Enviar Parcelas";
            buttonParcelas.UseVisualStyleBackColor = true;
            buttonParcelas.Click += buttonParcelas_Click;
            // 
            // buttonCancelLoop
            // 
            buttonCancelLoop.Location = new Point(132, 22);
            buttonCancelLoop.Name = "buttonCancelLoop";
            buttonCancelLoop.Size = new Size(120, 30);
            buttonCancelLoop.TabIndex = 1;
            buttonCancelLoop.Text = "Cancelar Loop";
            buttonCancelLoop.UseVisualStyleBackColor = true;
            buttonCancelLoop.Click += buttonCancelLoop_Click;
            // 
            // buttonStartLoop
            // 
            buttonStartLoop.Location = new Point(6, 21);
            buttonStartLoop.Name = "buttonStartLoop";
            buttonStartLoop.Size = new Size(120, 30);
            buttonStartLoop.TabIndex = 0;
            buttonStartLoop.Text = "Iniciar Loop";
            buttonStartLoop.UseVisualStyleBackColor = true;
            buttonStartLoop.Click += buttonStartLoop_Click;
            // 
            // buttonSendAll
            // 
            buttonSendAll.Location = new Point(258, 22);
            buttonSendAll.Name = "buttonSendAll";
            buttonSendAll.Size = new Size(120, 30);
            buttonSendAll.TabIndex = 2;
            buttonSendAll.Text = "Enviar Todos";
            buttonSendAll.UseVisualStyleBackColor = true;
            buttonSendAll.Click += buttonSendAll_Click;
            // 
            // button1EnvioInicial
            // 
            button1EnvioInicial.Location = new Point(384, 22);
            button1EnvioInicial.Name = "button1EnvioInicial";
            button1EnvioInicial.Size = new Size(120, 30);
            button1EnvioInicial.TabIndex = 5;
            button1EnvioInicial.Text = "Envio Inicial";
            button1EnvioInicial.UseVisualStyleBackColor = true;
            button1EnvioInicial.Click += button1EnvioInicial_Click;
            // 
            // Form1
            // 
            ClientSize = new Size(677, 450);
            Controls.Add(groupBoxActions);
            Controls.Add(textBoxResponse);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "LSI Gestor";
            Load += Form1_Load;
            contextMenuStrip1.ResumeLayout(false);
            groupBoxActions.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        private System.Windows.Forms.TextBox textBoxResponse;
        private FlowLayoutPanel flowLayoutPanel;
        private Button buttonSend4;
        private Button buttonSend;
        private Button buttonSendClientes;
        private Button buttonSend1;
        private Button buttonSend2;
        private Button buttonSendMovimentoCaixa;
        private Button buttonSendNotaFiscal;
        private Button buttonSend3;
        private NotifyIcon LsiGestor;
        private TextBox textBox1;
        private TextBox textBox2;
        private ToolStripMenuItem sAIRToolStripMenuItem;
        private ToolStripMenuItem sairToolStripMenuItem1;
        private ContextMenuStrip contextMenuStrip1;
        private ToolStripMenuItem sairToolStripMenuItem;
        private NotifyIcon notifyIcon1;
        private GroupBox groupBoxActions;
        private Button buttonCancelLoop;
        private Button buttonStartLoop;
        private Button buttonParcelas;
        private Button buttonSendAll;
        private Button button1EnvioInicial;
        private Button button1;
    }
}
