using Newtonsoft.Json;
using System.Data.SqlClient;
using System.Text;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;
using System.Data;
using System.Data.Common;
using FirebirdSql.Data.FirebirdClient;

namespace LsiGestor
{
    public partial class Form1 : Form
    {
        private int _isExecuting = 0; // 0 = livre, 1 = executando

        private static string connectionString;
        private bool isRunning = true;
        private TimeSpan interval = TimeSpan.FromMinutes(10); // tempo de envio
        private CancellationTokenSource cancellationTokenSource;
        private ContextMenuStrip contextMenuStrip;
        private int idEmpresa_TERMINAL;
        private int idEmpresa_API;
        private static string apiUrl;
        private string initialSendConfigFilePath = "initialSendConfig.txt"; // Caminho para o arquivo de configura��o de envio inicial
        public enum TipoBanco
        {
            SqlServer,
            Firebird
        }
        private static TipoBanco tipoBanco;

        public Form1()
        {
            InitializeComponent();
            this.SizeChanged += Form1_Resize;
            this.Shown += Form1_Shown; // <- aqui

            LoadConnectionString();
            LoadConnectionConexaoString();
        }
        private async void Form1_Shown(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;

            // Carrega as configura��es do banco de dados
            await LoadApiUrlFromDatabase();

            // Verifica se o envio inicial j� foi feito
            CheckInitialSendStatus();

            // N�O iniciar o loop automaticamente
            buttonStartLoop_Click(null, null);  
        }

        private void CheckInitialSendStatus()
        {
            if (File.Exists(initialSendConfigFilePath))
            {
                string content = File.ReadAllText(initialSendConfigFilePath);
                if (content.Trim() == "InitialSendDone")
                {
                    // Envio inicial j� foi feito
                    UpdateButtonStatesAfterInitialSend();
                }
                else
                {
                    // Conte�do do arquivo n�o � esperado, trata como se o envio inicial n�o tivesse sido feito
                    SetButtonStatesForInitialLoad();
                }
            }
            else
            {
                // Envio inicial n�o foi feito
                SetButtonStatesForInitialLoad();
            }
        }

        private async Task LoadApiUrlFromDatabase()
        {
            string query = "SELECT * FROM PARAMETROS_GESTOR";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                apiUrl = reader["URL_API"].ToString();
                                idEmpresa_API = Convert.ToInt32(reader["ID_EMPRESA_API"]);
                                idEmpresa_TERMINAL = Convert.ToInt32(reader["ID_ECF_EMPRESA"]);

                                if (reader["TEMPO_SINCRONIZACAO"] != DBNull.Value)
                                {
                                    int tempoSincronizacao = Convert.ToInt32(reader["TEMPO_SINCRONIZACAO"]);
                                    interval = TimeSpan.FromMinutes(tempoSincronizacao);
                                }

                                if (string.IsNullOrWhiteSpace(apiUrl))
                                {
                                    string cnpj = GetCnpjFromDatabase(idEmpresa_TERMINAL);
                                    if (!string.IsNullOrWhiteSpace(cnpj))
                                    {
                                        var apiData = await GetUrlAndIdFromApiAsync(cnpj);
                                        if (!string.IsNullOrWhiteSpace(apiData.url))
                                        {
                                            InsertApiUrl(apiData.url);
                                            if (idEmpresa_API == 0 || reader["ID_EMPRESA_API"] == DBNull.Value)
                                            {
                                                InsertApiID(apiData.id);
                                            }
                                            // Recarrega as configura��es ap�s inserir a URL da API
                                            await LoadApiUrlFromDatabase();
                                        }
                                        else
                                        {
                                            Console.WriteLine("A URL da API obtida est� vazia ou nula.");
                                            MessageBox.Show("N�o foi poss�vel obter a URL da API.");
                                            Application.Exit();
                                        }
                                    }
                                    else
                                    {
                                        MessageBox.Show("CNPJ n�o encontrado.");
                                        Application.Exit();
                                    }
                                }
                                else if (idEmpresa_API == 0 || reader["ID_EMPRESA_API"] == DBNull.Value)
                                {
                                    var apiData = await GetUrlAndIdFromApiAsync(apiUrl);
                                    InsertApiID(apiData.id);
                                }
                            }
                            else
                            {
                                MessageBox.Show("Configura��es n�o encontradas.");
                                Application.Exit();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao carregar as configura��es do banco de dados: {ex.Message}");
                    Application.Exit();
                }
            }
        }

        private string GetCnpjFromDatabase(int idEmpresa_TERMINAL)
        {
            string query = "SELECT CNPJ FROM ECF_EMPRESA WHERE ID = @idEmpresa_TERMINAL";
            string cnpj = null;

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@idEmpresa_TERMINAL", idEmpresa_TERMINAL);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                cnpj = reader["CNPJ"].ToString();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao buscar o CNPJ no banco de dados: {ex.Message}");
                    Application.Exit();
                }
            }

            return cnpj;
        }

        private async Task<(string url, string id)> GetUrlAndIdFromApiAsync(string cnpjOrUrl)
        {
            string apiUrl = "https://app.meuclickonline.com.br/rest.php?class=CadastrosRestService&method=consulta_empresa";
            var requestData = new { cnpj = cnpjOrUrl };

            try
            {
                // Usa apenas o HttpClient est�tico
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!httpClient.DefaultRequestHeaders.Contains("Authorization"))
                {
                    httpClient.DefaultRequestHeaders.Add(
                        "Authorization",
                        "Basic_1927b11f4d4186c2f92d04a25956a41ed5c93909b350560e31b8b5719b43"
                    );
                }


                var json = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(apiUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    string url = result?.data?.url?.ToString();
                    string id = result?.data?.id?.ToString();

                    if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(id))
                        throw new Exception("URL ou ID da API n�o encontrados na resposta.");

                    return (url, id);
                }
                else
                {
                    throw new Exception($"Erro: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao obter URL e ID da API: {ex.Message}");
                return (null, null);
            }
        }


        private void InsertApiUrl(string apiUrl)
        {
            string updateQuery = "UPDATE PARAMETROS_GESTOR SET URL_API = @url WHERE ID_ECF_EMPRESA = @idEmpresa_TERMINAL";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    using (SqlCommand command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@url", apiUrl);
                        command.Parameters.AddWithValue("@idEmpresa_TERMINAL", idEmpresa_TERMINAL);
                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao atualizar a URL da API no banco de dados: {ex.Message}");
                    Application.Exit();
                }
            }
        }
        private void InsertApiID(string id)
        {
            string updateQuery = "UPDATE PARAMETROS_GESTOR SET ID_EMPRESA_API = @idAPI WHERE ID_ECF_EMPRESA = @idEmpresa_TERMINAL";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    using (SqlCommand command = new SqlCommand(updateQuery, connection))
                    {
                        command.Parameters.AddWithValue("@idAPI", id);
                        command.Parameters.AddWithValue("@idEmpresa_TERMINAL", idEmpresa_TERMINAL);
                        command.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao atualizar o ID da API no banco de dados: {ex.Message}");
                    Application.Exit();
                }
            }
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                notifyIcon1.Visible = true;
                notifyIcon1.ShowBalloonTip(1000); // Mostrar dica de bal�o
            }
        }
        private void LoadConnectionConexaoString()
        {
            if (File.Exists("Conexao.conf"))
            {
                try
                {
                    string[] linhas = File.ReadAllLines("Conexao.conf");

                    string servidor = null, banco = null, usuario = null, senha = null, tipoBanco = null;
                    bool dentroGestor = false;

                    foreach (string linha in linhas)
                    {
                        if (linha.Trim().StartsWith("[GESTOR]"))
                        {
                            dentroGestor = true;
                        }
                        else if (linha.StartsWith("[") && linha.EndsWith("]"))
                        {
                            dentroGestor = false;
                        }
                        else if (dentroGestor)
                        {
                            if (linha.StartsWith("SERVIDOR="))
                            {
                                servidor = linha.Substring("SERVIDOR=".Length);
                            }
                            else if (linha.StartsWith("BANCO="))
                            {
                                banco = linha.Substring("BANCO=".Length);
                            }
                            else if (linha.StartsWith("USUARIO="))
                            {
                                usuario = linha.Substring("USUARIO=".Length);
                            }
                            else if (linha.StartsWith("SENHA="))
                            {
                                senha = linha.Substring("SENHA=".Length);
                            }
                            else if (linha.StartsWith("TIPO_BANCO="))
                            {
                                tipoBanco = linha.Substring("TIPO_BANCO=".Length);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(servidor) &&
                        !string.IsNullOrEmpty(banco) &&
                        !string.IsNullOrEmpty(usuario) &&
                        !string.IsNullOrEmpty(senha) &&
                        !string.IsNullOrEmpty(tipoBanco))
                    {
                        if (tipoBanco.ToUpper() == "SQLSERVER")
                        {
                            Form1.tipoBanco = TipoBanco.SqlServer;
                            connectionString =
                                $"Data Source={servidor};Initial Catalog={banco};User ID={usuario};Password={senha};";
                        }
                        else if (tipoBanco.ToUpper() == "FIREBIRD")
                        {
                            Form1.tipoBanco = TipoBanco.Firebird;
                            connectionString =
                                $"Database={banco};DataSource={servidor};User={usuario};Password={senha};Charset=UTF8;";
                        }
                        else
                        {
                            MessageBox.Show("TIPO_BANCO inválido. Use SQLSERVER ou FIREBIRD.");
                            Application.Exit();
                        }
                    }

                    else
                    {
                        MessageBox.Show("As informa��es do banco de dados n�o foram encontradas ou est�o incompletas.");
                        Application.Exit();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ocorreu um erro ao ler o arquivo de configura��o Conexao.conf: {ex.Message}");
                    Application.Exit();
                }
            }
            else
            {
                MessageBox.Show("O arquivo de configura��o Conexao.conf n�o foi encontrado.");
                Application.Exit();
            }
        }
        private DbConnection CreateConnection()
        {
            if (tipoBanco == TipoBanco.SqlServer)
                return new SqlConnection(connectionString);

            if (tipoBanco == TipoBanco.Firebird)
                return new FirebirdSql.Data.FirebirdClient.FbConnection(connectionString);

            throw new NotSupportedException("Banco de dados não suportado.");
        }

        private void LoadConnectionString()
        {
            if (File.Exists("Terminal.conf"))
            {
                try
                {
                    string[] linhas = File.ReadAllLines("Terminal.conf");

                    int idEmpresa_TERMINAL = 0;
                    bool dentroECFS = false;

                    foreach (string linha in linhas)
                    {
                        if (linha.Trim().StartsWith("[ECFS]"))
                        {
                            dentroECFS = true;
                        }
                        else if (linha.StartsWith("[") && linha.EndsWith("]"))
                        {
                            dentroECFS = false;
                        }
                        else if (dentroECFS)
                        {
                            if (linha.StartsWith("EMPRESA="))
                            {
                                int.TryParse(linha.Substring("EMPRESA=".Length), out idEmpresa_TERMINAL);
                            }
                        }
                    }

                    if (idEmpresa_TERMINAL != 0)
                    {
                        idEmpresa_TERMINAL = idEmpresa_TERMINAL;
                    }
                    else
                    {
                        MessageBox.Show("A informa��o de EMPRESA n�o foi encontrada ou est� inv�lida.");
                        Application.Exit();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ocorreu um erro ao ler o arquivo de configura��o Terminal.conf: {ex.Message}");
                    Application.Exit();
                }
            }
            else
            {
                MessageBox.Show("O arquivo de configura��o Terminal.conf n�o foi encontrado.");
                Application.Exit();
            }
        }

        private async Task BackgroundWorkerAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // evita reentrância (não deixa executar se ainda não terminou)
                    if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) == 0)
                    {
                        try
                        {
                            await ExecuteSendMethods();
                        }
                        catch (Exception ex)
                        {
                            UpdateResponseTextBox($"Erro durante o envio: {ex.Message}");
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _isExecuting, 0);
                        }
                    }

                    await Task.Delay(TimeSpan.FromMinutes(10), cancellationToken); // loop a cada 1 minuto fixo
                }
            }
            catch (OperationCanceledException)
            {
                UpdateResponseTextBox("Operação cancelada.");
            }
        }



        private void buttonStartLoop_Click(object sender, EventArgs e)
        {
            cancellationTokenSource?.Cancel();

            cancellationTokenSource = new CancellationTokenSource();

            _ = Task.Run(() => BackgroundWorkerAsync(cancellationTokenSource.Token));
        }


        private async void buttonCancelLoop_Click(object sender, EventArgs e)
        {
            isRunning = false; // Indica que o loop deve parar
            cancellationTokenSource?.Cancel(); // Cancela qualquer opera��o em andamento
            textBoxResponse.Clear(); // Limpa o painel de respostas da API
        }

        private async void buttonSendAll_Click(object sender, EventArgs e)
        {
            await ExecuteSendMethods2();
        }
        private async void buttonParcelas_Click(object sender, EventArgs e)
        {
           // await ExecuteSendParcelas();
        }

        private async Task ExecuteSendMethods()
        {
            textBoxResponse.Clear();
            LoadConnectionString();

            try
            {
                var apiData = idEmpresa_API;

                if (string.IsNullOrEmpty(apiUrl) || idEmpresa_API == 0)
                {
                    MessageBox.Show("N�o foi poss�vel obter a URL da API ou o ID da empresa.");
                    return;
                }

                UpdateResponseTextBox("Enviando dados...");

                // Envio dos dados em pacotes
                UpdateResponseTextBox("Aguarde, realizando upload de Clientes...");
                await SendClientes(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Clientes");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Formas de Pagamento...");
                await SendFormaPagamento(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Formas de Pagamento");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Grupo de Pagamento...");
                await SendGrupoPagamento(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Formas de Grupo Pagamento");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Funcion�rios...");
                await SendFuncionarios(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Funcion�rios");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de PDV...");
                await SendPdv(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para PDV");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Grupo de Produtos...");
                await SendProdutos(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Grupo de Produtos");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Produtos...");
                await SendProdutosL2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Produtos");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Movimento de Caixa...");
                await SendMovimentoCaixa(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Movimento de Caixa");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Nota Fiscal Cabe�alho...");
                await SendNotaFiscalCabecalho(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Nota Fiscal Cabe�alho");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Estoque...");
                await SendQtdEstoque(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Estoque");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Promo��o...");
                await SendCampanhaPromocao(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Promo��o");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Promo��o dos Produtos...");
                await SendProdutoPromocao(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Promo��o dos Produtos");
                textBoxResponse.Clear();

/*                UpdateResponseTextBox("Aguarde, realizando upload de Parcelas...");
                await SendParcelas(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Parcelas");*/
                // textBoxResponse.Clear();

                /*                UpdateResponseTextBox("Aguarde, realizando upload de Parcelas Detalhe...");
                                await SendParcelasDetalhe(idEmpresa_API);
                                UpdateResponseTextBox("Upload realizado para Parcelas Detalhe");
                                //textBoxResponse.Clear();

                                UpdateResponseTextBox("Aguarde, realizando upload de Parcelas Pagamento...");
                                await SendParcelasPagamento(idEmpresa_API);
                                UpdateResponseTextBox("Upload realizado para Parcelas Pagamento");
                                //textBoxResponse.Clear();*/
                //
                // textBoxResponse.Clear();
                UpdateResponseTextBox("Todos os Uploads Realizados");
            }
            catch (Exception ex)
            {
                UpdateResponseTextBox($"Erro durante o envio: {ex.Message}");
            }
        }

        private async Task SendClientes(int idEmpresa_API)
        {
            var clientes = GetClientesFromDatabase();
            foreach (var cliente in clientes)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                new
                {
                    cliente.id,
                    cliente.razao_social,
                    cliente.cnpj,
                    cliente.nome_fantasia,
                    cliente.grupo_cliente,
                    cliente.cidade,
                    cliente.data_nascimento,
                    cliente.bairro
                }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_cliente",
                    dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateClienteStatus(cliente.id);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }
        private async Task SendFormaPagamento(int idEmpresa_API)
        {
            var formaPagamento = GetFormaPagamentoFromDatabase();
            foreach (var pagamento in formaPagamento)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                new
                {
                    pagamento.id,
                    pagamento.descricao,
                    pagamento.percentual_comissao,
                    pagamento.grupo_pagamento_id
                }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_forma_pagamento", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateFormaPagamentoStatus(pagamento.id);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendGrupoPagamento(int idEmpresa_API)
        {
            var grupoPagamento = GetGrupoPagamentoFromDatabase();
            foreach (var grupo in grupoPagamento)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                new
                {
                    grupo.id,
                    grupo.descricao
                }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_grupo_pagamento", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateGrupoPagamentoStatus(grupo.id);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendFuncionarios(int idEmpresa_API)
        {
            var funcionarios = GetFuncionarioFromDatabase();
            foreach (var funcionario in funcionarios)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                new
                {
                    funcionario.id,
                    funcionario.descricao,
                    funcionario.senha,
                    funcionario.caixa,
                    funcionario.vendedor,
                    funcionario.login
                }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_funcionario", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateFuncionarioStatus(funcionario.id);
                }


                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendPdv(int idEmpresa_API)
        {
            var pdvs = GetPdvFromDatabase();
            foreach (var pdv in pdvs)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                new
                {
                    pdv.id,
                    pdv.descricao
                }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_terminal_pdv", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdatePdvStatus(pdv.id);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendProdutos(int idEmpresa_API)
        {
            var produtos = GetProdutosFromDatabase();
            foreach (var produto in produtos)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                new
                {
                    produto.id,
                    produto.descricao,
                    produto.percentual_comissao
                }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_grupo_produto", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateProdutoStatus(produto.id);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendProdutosL(int idEmpresa_API)
        {
            int batchSize = GetBatchSizeFromParametrosGestor(); // Obt�m o tamanho do lote da tabela
            var produtosL = GetProdutosLFromDatabase();
            int totalProducts = produtosL.Length;
            int numberOfBatches = (totalProducts + batchSize - 1) / batchSize; // Calcula o n�mero de lotes necess�rio

            UpdateResponseTextBox($"Total de Produtos: {totalProducts}");

            bool allBatchesSuccessful = true; // Flag para verificar se todos os lotes foram enviados com sucesso

            for (int batchNumber = 0; batchNumber < numberOfBatches; batchNumber++)
            {
                int start = batchNumber * batchSize;
                int end = Math.Min(start + batchSize, totalProducts);

                var currentBatch = produtosL.Skip(start).Take(end - start).ToArray();

                UpdateResponseTextBox($"Enviando lote {batchNumber + 1} contendo {currentBatch.Length} produtos...");

                try
                {
                    var dados = new { id_empresa = idEmpresa_API, dados = currentBatch };
                    var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_produto", dados);

                    if (responseContent.Contains("success"))
                    {
                        UpdateResponseTextBox($"Lote {batchNumber + 1} de {numberOfBatches} enviado com sucesso.");
                    }
                    else
                    {
                        UpdateResponseTextBox($"Lote {batchNumber + 1} de {numberOfBatches} teve problemas: {responseContent}");
                        allBatchesSuccessful = false; // Marcar flag como falsa se houver problemas
                    }
                }
                catch (Exception ex)
                {
                    UpdateResponseTextBox($"Erro ao enviar lote {batchNumber + 1}: {ex.Message}");
                    allBatchesSuccessful = false; // Marcar flag como falsa se houver exce��o
                }
            }

            UpdateResponseTextBox("Envio de todos os lotes conclu�do.");

            // Atualizar o status do produto somente se todos os lotes forem enviados com sucesso
            if (allBatchesSuccessful)
            {
                foreach (var produto in produtosL)
                {
                    UpdateProdutoLStatus(produto.id);
                }
            }
            else
            {
                UpdateResponseTextBox("Houve problemas no envio de alguns lotes. Status dos produtos n�o atualizado.");
            }
        }

        private async Task SendQtdEstoque(int idEmpresa_API)
        {
            var qtdEstoques = GetQtdEstoqueFromDatabase();
            foreach (var qtdEstoque in qtdEstoques)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                new
                {
                    qtdEstoque.id_produto,
                    qtdEstoque.qtd_estoque
                }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_estoque_produto", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateQtdEstoque(qtdEstoque.id_produto);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendCampanhaPromocao(int idEmpresa_API)
        {
            var promocoes = GetCampanhaPromocaoFromDatabase();
            foreach (var promocao in promocoes)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                        new
                        {
                            promocao.id,
                            promocao.descricao,
                            promocao.data_inicial,
                            promocao.data_final,
                            promocao.status,
                            promocao.empresa_id,
                            promocao.id_tipo_preco_produto
                        }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_campanha", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateCampanhaPromocao(promocao.id);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendProdutoPromocao(int idEmpresa_API)
        {
            var produtopromos = GetProdutoPromocaoFromDatabase();
            foreach (var produtopromo in produtopromos)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                        new
                        {
                            produtopromo.id,
                            produtopromo.produto_id,
                            produtopromo.quantidade_em_promocao,
                            produtopromo.quantidade_maxima_cliente,
                            produtopromo.valor,
                            produtopromo.id_campanha_promo_prod,
                            produtopromo.qtda_vendida,
                            produtopromo.id_tipo_estoque_produto,
                            produtopromo.valor_venda
                        }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_produto_promocao", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateProdutoPromocao(produtopromo.id);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendParcelas(int idEmpresa_API)
        {
            int batchSize = GetBatchSizeFromParametrosGestor(); // Obt�m o tamanho do lote da tabela

            var parcelas = GetCabecalhoParcela();
            for (int i = 0; i < parcelas.Length; i += batchSize)
            {
                var batchParcelas = parcelas.Skip(i).Take(batchSize).ToArray();

                // Processa e envia o lote atual
                foreach (var parcela in batchParcelas)
                {
                    var detalhes = GetParcelasDetalhes(parcela.id);
                    var pagamentos = GetParcelasPagamentos(parcela.id);

                    var dados = new
                    {
                        id_empresa = idEmpresa_API,
                        cabecalho = new[]
                        {
                            new
                            {
                                parcela.id,
                                id_empresa = idEmpresa_API,
                                parcela.id_terminal_pdv,
                                parcela.historico,
                                parcela.id_tipo_pagamento,
                                parcela.id_plano_conta,
                                parcela.id_pessoa,
                                parcela.tipo,
                                parcela.numero_documento,
                                parcela.valor_total,
                                parcela.data_lancamento,
                                parcela.primeiro_vencimento,
                                parcela.quantidade_parcela,
                                parcela.id_movimentacao,
                                parcela.intervalo_vencimento,
                                parcela.id_centro_custo,
                                parcela.status_previsao,
                                parcela.fixa_vencimento,
                                parcela.boleto_impresso,
                                parcela.stbaixa_retorno,
                                parcela.status_estorno,
                                detalhes,
                                pagamento = pagamentos
                            }
                        }
                    };

                    var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_parcela_completa", dados);

                    UpdateResponseTextBox(FormatApiResponse(responseContent));
                }
            }
        }
        private async Task SendParcelasDetalhe(int idEmpresa_API)
        {
            int batchSize = GetBatchSizeFromParametrosGestor();
            var detalhes = GetParcelasDetalhes2();

            for (int i = 0; i < detalhes.Count; i += batchSize)
            {
                var batchDetalhes = detalhes.Skip(i).Take(batchSize).ToList();

                foreach (var det in batchDetalhes)
                {
                    // Monta o JSON correto
                    var dados = new
                    {
                        id_parcela = det.id_contas_parcelas,
                        detalhes = new[]
                        {
                            new
                            {
                                id = det.id,
                                data_vencimento = det.data_vencimento,
                                data_pagamento = det.data_pagamento,
                                numero_parcela = det.numero_parcela,
                                valor = det.valor,
                                taxa_juros = det.taxa_juros,
                                taxa_multa = det.taxa_multa,
                                taxa_desconto = det.taxa_desconto,
                                valor_juros = det.valor_juros,
                                id_situacao_parcela = det.id_situacao_parcela,
                                valor_pago = det.valor_pago,
                                id_empresa_pago = det.id_empresa_pago,
                                boleto_impresso = det.boleto_impresso,
                                stbaixa_retorno = det.stbaixa_retorno,
                                nosso_numero = det.nosso_numero,
                                status_estorno = det.status_estorno,
                                status_previsao = det.status_previsao
                            }
                        }
                    };

                    // Chamada correta
                    var responseContent = await PostAsync(
                        $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_contas_detalhe",
                        dados
                    );

                    if (responseContent.Contains("\"status\":\"success\""))
                    {
                        UpdateParcelaStatus(det.id, "CONTAS_DETALHE");
                    }

                    UpdateResponseTextBox(FormatApiResponse(responseContent));
                }
            }
        }

        private async Task SendParcelasPagamento(int idEmpresa_API)
        {
            int batchSize = GetBatchSizeFromParametrosGestor();
            var pagamentos = GetParcelasPagamentos2();

            for (int i = 0; i < pagamentos.Count; i += batchSize)
            {
                var batchPagamentos = pagamentos.Skip(i).Take(batchSize).ToList();

                foreach (var pag in batchPagamentos)
                {
                    var dados = new
                    {
                        id_empresa = idEmpresa_API, // envia a empresa
                        pagamentos = new[]
                        {
                    new
                    {
                        id = pag.id,
                        id_contas_detalhes = pag.id_contas_detalhes, // será usado como id_banco_local
                        id_ecf_tipo_pagamento = pag.id_ecf_tipo_pagamento,
                        valor_pagamento = pag.valor_pagamento,
                        data_estorno = pag.data_estorno?.ToString("yyyy-MM-dd HH:mm:ss"),
                        status_estorno = pag.status_estorno ?? "N",
                        id_original = pag.id_original,
                        id_terminal_pdv = pag.id_terminal_pdv,
                        tipo_baixa = pag.tipo_baixa,
                        id_cpag_terminal = pag.id_cpag_terminal,
                        id_conta_bancaria = pag.id_conta_bancaria
                    }
                }
                    };

                    var responseContent = await PostAsync(
                        $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_contas_pagamento",
                        dados
                    );

                    if (responseContent.Contains("\"status\":\"success\""))
                    {
                        UpdateParcelaStatus(pag.id, "CONTAS_PAGAMENTO");
                    }

                    UpdateResponseTextBox(FormatApiResponse(responseContent));
                }
            }
        }



        private dynamic[] GetCabecalhoParcela()
        {
            var parcelas = new List<dynamic>();


            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT 
                        v.ID, v.HISTORICO, v.ID_TIPO_PAGAMENTO, v.ID_PLANO_CONTA, v.ID_PESSOA, v.TIPO, v.NUMERO_DOCUMENTO,
                        v.VALOR_TOTAL, v.DATA_LANCAMENTO, v.PRIMEIRO_VENCIMENTO, v.QUANTIDADE_PARCELA, v.ID_MOVIMENTACAO, v.ID_EMPRESA, 
                        v.INTERVALO_VENCIMENTO, v.ID_CENTRO_CUSTO, v.STATUS_PREVISAO, v.FIXA_VENCIMENTO, v.ID_TERMINAL_PDV, v.BOLETO_IMPRESSO, 
                        v.STBAIXA_RETORNO, v.STATUS_ESTORNO
                    FROM CONTAS_PARCELAS v
                    INNER JOIN LOG_EXPORT_REPLI l ON l.ID_TABELA = v.ID
                    INNER JOIN CONTAS_DETALHE cd ON cd.ID_CONTAS_PARCELAS = v.ID
                    WHERE l.INTEGRADO = 'N' 
                      AND v.ID_EMPRESA = @idEmpresa 
                      AND l.TABELA = 'CONTAS_PARCELAS'
                      AND cd.ID_SITUACAO_PARCELA in (1,2)";

                    var pEmpresa = command.CreateParameter();
                    pEmpresa.ParameterName = "@idEmpresa";
                    pEmpresa.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(pEmpresa);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            parcelas.Add(new
                            {
                                id = reader["ID"] != DBNull.Value ? Convert.ToInt32(reader["ID"]) : 0,
                                idOriginal = reader["ID"] != DBNull.Value ? Convert.ToInt32(reader["ID"]) : 0,
                                historico = reader["HISTORICO"] as string,
                                id_tipo_pagamento = reader["ID_TIPO_PAGAMENTO"] != DBNull.Value ? Convert.ToInt32(reader["ID_TIPO_PAGAMENTO"]) : (int?)null,
                                id_plano_conta = reader["ID_PLANO_CONTA"] != DBNull.Value ? Convert.ToInt32(reader["ID_PLANO_CONTA"]) : 0,
                                id_pessoa = reader["ID_PESSOA"] != DBNull.Value ? Convert.ToInt32(reader["ID_PESSOA"]) : 0,
                                tipo = reader["TIPO"] as string,
                                numero_documento = reader["NUMERO_DOCUMENTO"] as string,
                                valor_total = reader["VALOR_TOTAL"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR_TOTAL"]) : 0m,
                                data_lancamento = reader["DATA_LANCAMENTO"] != DBNull.Value ? Convert.ToDateTime(reader["DATA_LANCAMENTO"]) : (DateTime?)null,
                                primeiro_vencimento = reader["PRIMEIRO_VENCIMENTO"] != DBNull.Value ? Convert.ToDateTime(reader["PRIMEIRO_VENCIMENTO"]) : (DateTime?)null,
                                quantidade_parcela = reader["QUANTIDADE_PARCELA"] != DBNull.Value ? Convert.ToInt32(reader["QUANTIDADE_PARCELA"]) : (int?)null,
                                id_movimentacao = reader["ID_MOVIMENTACAO"] != DBNull.Value ? Convert.ToInt32(reader["ID_MOVIMENTACAO"]) : 0,
                                id_empresa = reader["ID_EMPRESA"] != DBNull.Value ? Convert.ToInt32(reader["ID_EMPRESA"]) : 0,
                                intervalo_vencimento = reader["INTERVALO_VENCIMENTO"] != DBNull.Value ? Convert.ToInt32(reader["INTERVALO_VENCIMENTO"]) : (int?)null,
                                id_centro_custo = reader["ID_CENTRO_CUSTO"] != DBNull.Value ? Convert.ToInt32(reader["ID_CENTRO_CUSTO"]) : 0,
                                status_previsao = reader["STATUS_PREVISAO"] as string,
                                fixa_vencimento = reader["FIXA_VENCIMENTO"] as string,
                                id_terminal_pdv = reader["ID_TERMINAL_PDV"] != DBNull.Value ? Convert.ToInt32(reader["ID_TERMINAL_PDV"]) : 0,
                                boleto_impresso = reader["BOLETO_IMPRESSO"] as string,
                                stbaixa_retorno = reader["STBAIXA_RETORNO"] as string,
                                status_estorno = reader["STATUS_ESTORNO"] as string
                            });
                        }
                    }
                }
            }
            return parcelas.ToArray();
        }

        private async Task SendMovimentoCaixa(int idEmpresa_API)
        {
            var caixas = GetMovimentoCaixaFromDatabase();
            foreach (var caixa in caixas)
            {
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    dados = new[]
                    {
                new
                {
                    caixa.id,
                    caixa.descricao,
                    caixa.data_movimento,
                    caixa.valor,
                    caixa.funcionario_id,
                    caixa.tipo_movimento_caixa_id,
                    caixa.terminal_pdv_id
                }
            }
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_movimento_caixa", dados);

                if (responseContent.Contains("\"status\":\"success\""))
                {
                    UpdateMovimentoCaixaStatus(caixa.id);
                }

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendNotaFiscalCabecalho(int idEmpresa_API)
        {
            int batchSize = GetBatchSizeFromParametrosGestor();

            var notasFiscais = GetNotaFiscalCabecalhoFromDatabase();
            for (int i = 0; i < notasFiscais.Length; i += batchSize)
            {
                var batchNotasFiscais = notasFiscais.Skip(i).Take(batchSize).ToArray();
                var loteDados = new List<object>();

                foreach (var nota in batchNotasFiscais)
                {
                    /*                    string conteudoXml = "";
                                        string fileId = "";
                                        bool deveSubirXml = false;
                                        string caminhoArquivo = "";

                                        // Verifica regras para envio de XML
                                        if ((nota.tipo_nota_fiscal_id == 1 || nota.tipo_nota_fiscal_id == 7 || nota.tipo_nota_fiscal_id == 9)
                                            && nota.idSituacaoNF == 4) // NF-e
                                        {
                                            deveSubirXml = true;

                                            caminhoArquivo = $@"{nota.caminhoNFE}{nota.chave_acesso}-nfe.xml";
                                        }
                                        else if ((nota.tipo_nota_fiscal_id == 2 || nota.tipo_nota_fiscal_id == 8)
                                                 && nota.idStatusNFCe == 3) // NFC-e
                                        {
                                            deveSubirXml = true;
                                            // CORRE��O: Apenas atribui o valor, n�o declara a vari�vel novamente.
                                            caminhoArquivo = $@"{nota.caminhoNFCE}{nota.chave_acesso}-nfe.xml";
                                        }

                                        if (deveSubirXml)
                                        {
                                            // CORRE��O: A declara��o redundante foi removida daqui.

                                            if (File.Exists(caminhoArquivo))
                                            {
                                                conteudoXml = File.ReadAllText(caminhoArquivo);

                                                var xmlPayload = new
                                                {
                                                    id_empresa = idEmpresa_API,
                                                    xml = conteudoXml
                                                };

                                                var xmlResponse = await PostAsync($"{apiUrl}/rest.php?class=XmlService&method=uploadXML", xmlPayload);

                                                dynamic xmlResult = JsonConvert.DeserializeObject(xmlResponse);
                                                if (xmlResult.status == "success")
                                                {
                                                    fileId = xmlResult.data.fileId;
                                                    UpdateNotaFiscalCabecalhoXmlNuvem(nota.id, fileId);
                                                }
                                                else
                                                {
                                                    UpdateResponseTextBox($"Erro ao enviar XML da nota {nota.id}: {xmlResponse}");
                                                }
                                            }
                                            else
                                            {
                                                UpdateResponseTextBox($"Arquivo XML n�o encontrado: {caminhoArquivo}");
                                            }
                                        }*/

                    // Sempre monta os dados da nota, mesmo que n�o suba XML
                    var itens = GetNotaFiscalDetalhesFromDatabase(nota.id);
                    var pagamentos = GetNotaFiscalPagamentoFromDatabase(nota.id);

                    loteDados.Add(new
                    {
                        nota.id,
                        nota.observacao,
                        nota.tipo_nota_fiscal_id,
                        nota.cliente_id,
                        nota.terminal_pdv_id,
                        nota.situacao_movimentacao_id,
                        nota.data_autorizacao,
                        nota.data_venda,
                        nota.nome_cliente,
                        nota.numero,
                        nota.serie,
                        nota.total_produtos,
                        nota.total_servicos,
                        nota.total_acrescimo,
                        nota.total_desconto,
                        nota.total_geral,
                        nota.valor_icms,
                        nota.valor_pis,
                        nota.valor_cofins,
                        nota.valor_ipi,
                        nota.valor_iss,
                        nota.vendedor_id,
                        nota.caixa_id,
                        nota.total_nf,
                        //nota.chave_acesso,
                        itens,
                        pagamento = pagamentos
                        //chave_api_storage = fileId
                    });
                }

                // Envio do lote para a API
                if (loteDados.Any())
                {
                    var dados = new
                    {
                        id_empresa = idEmpresa_API,
                        dados = loteDados.ToArray()
                    };

                    var responseContent = await PostAsync(
                        $"{apiUrl}/rest.php?class=NotaRestService&method=upload_nota_fiscal",
                        dados
                    );

                    try
                    {
                        // Tenta identificar se o retorno foi sucesso
                        if (responseContent.Contains("\"status\":\"success\""))
                        {
                            foreach (var nota in batchNotasFiscais)
                            {
                                UpdateNotaFiscalCabecalhoStatus(nota.id);
                            }
                        }
                        else
                        {
                            WriteLogToFile($"[ERRO AO ENVIAR LOTE] Retorno da API: {responseContent}");
                        }

                        // Tamb�m grava a vers�o formatada (caso queira acompanhar JSON leg�vel)
                        WriteLogToFile(FormatApiResponse(responseContent));
                    }
                    catch (Exception ex)
                    {
                        WriteLogToFile($"Erro ao processar resposta da API: {ex.Message}");
                    }


                }
            }
        }
        private void WriteLogToFile(string message)
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string logFilePath = Path.Combine(logDirectory, "LogNotasFiscais.txt");
                string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";

                File.AppendAllText(logFilePath, logMessage);
            }
            catch (Exception ex)
            {
                // Caso d� erro ao gravar o log (por permiss�o, etc.)
                Console.WriteLine($"Erro ao escrever log: {ex.Message}");
            }
        }

        private async Task SendParcelas2(int idEmpresa_API)
        {
            int batchSize = GetBatchSizeFromParametrosGestor();

            var parcelas = GetCabecalhoParcela2();

            for (int i = 0; i < parcelas.Length; i += batchSize)
            {
                var batchParcelas = parcelas.Skip(i).Take(batchSize).ToArray();

                // Monta o lote completo de cabeçalhos para enviar tudo de uma vez
                var cabecalhos = batchParcelas.Select(parcela =>
                {
                    var detalhes = GetParcelasDetalhes(parcela.id);
                    var pagamentos = GetParcelasPagamentos(parcela.id);

                    return new
                    {
                        parcela.id,
                        id_empresa = idEmpresa_API,
                        parcela.id_terminal_pdv,
                        parcela.historico,
                        parcela.id_tipo_pagamento,
                        parcela.id_plano_conta,
                        parcela.id_pessoa,
                        parcela.tipo,
                        parcela.numero_documento,
                        parcela.valor_total,
                        parcela.data_lancamento,
                        parcela.primeiro_vencimento,
                        parcela.quantidade_parcela,
                        parcela.id_movimentacao,
                        parcela.intervalo_vencimento,
                        parcela.id_centro_custo,
                        parcela.status_previsao,
                        parcela.fixa_vencimento,
                        parcela.boleto_impresso,
                        parcela.stbaixa_retorno,
                        parcela.status_estorno,
                        detalhes,
                        pagamento = pagamentos
                    };
                }).ToArray();

                // envia apenas UMA vez por lote
                var dados = new
                {
                    id_empresa = idEmpresa_API,
                    cabecalho = cabecalhos
                };

                var responseContent = await PostAsync(
                    $"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_parcela_completa",
                    dados
                );

                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private void UpdateNotaFiscalCabecalhoXmlNuvem(int notaId, string fileId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    UPDATE NOTA_FISCAL_CABECALHO
                    SET XML_NUVEM = @fileId
                    WHERE id = @notaId";

                    var pFile = command.CreateParameter();
                    pFile.ParameterName = "@fileId";
                    pFile.Value = fileId;
                    command.Parameters.Add(pFile);

                    var pNota = command.CreateParameter();
                    pNota.ParameterName = "@notaId";
                    pNota.Value = notaId;
                    command.Parameters.Add(pNota);
                    command.ExecuteNonQuery();
                }
            }
        }
        private void UpdateFuncionarioStatus(int funcionarioId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE 
                        LOG_EXPORT_CLOUD
                        SET INTEGRADO = 'S'
                        WHERE TABELA = 'ECF_FUNCIONARIO'
                        AND ID_TABELA = @id;";

                    var pFuncionario = command.CreateParameter();
                    pFuncionario.ParameterName = "@id";
                    pFuncionario.Value = funcionarioId;
                    command.Parameters.Add(pFuncionario);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateProdutoStatus(int produtoId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE 
                        LOG_EXPORT_CLOUD
                        SET INTEGRADO = 'S'
                        WHERE TABELA = 'GRUPO_PRODUTO'
                        AND ID_TABELA = @id;";

                    var pProduto = command.CreateParameter();
                    pProduto.ParameterName = "@id";
                    pProduto.Value = produtoId;
                    command.Parameters.Add(pProduto);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateMovimentoCaixaStatus(int caixaId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    UPDATE LOG_EXPORT_CLOUD
                    SET INTEGRADO = 'S'
                    WHERE TABELA IN ('ECF_SANGRIA', 'ECF_SUPRIMENTO')
                    AND ID_TABELA = @id;";

                    var pCaixa = command.CreateParameter();
                    pCaixa.ParameterName = "@id";
                    pCaixa.Value = caixaId;
                    command.Parameters.Add(pCaixa);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateProdutoLStatus(int produtoLId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE 
                        LOG_EXPORT_CLOUD
                        SET INTEGRADO = 'S'
                        WHERE TABELA = 'PRODUTO'
                        AND ID_TABELA = @id;";

                    var pProduto = command.CreateParameter();
                    pProduto.ParameterName = "@id";
                    pProduto.Value = produtoLId;
                    command.Parameters.Add(pProduto);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateClienteStatus(int clienteId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE 
                        LOG_EXPORT_CLOUD
                        SET INTEGRADO = 'S'
                        WHERE TABELA = 'Cliente'
                        AND ID_TABELA = @id;";

                    var pCliente = command.CreateParameter();
                    pCliente.ParameterName = "@id";
                    pCliente.Value = clienteId;
                    command.Parameters.Add(pCliente);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateCampanhaPromocao(int promocaoID)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE 
                        LOG_EXPORT_CLOUD
                        SET INTEGRADO = 'S'
                        WHERE TABELA = 'CAMPANHA_PROMOCAO_PRODUTO'
                        AND ID_TABELA = @id;";

                    var pPromo = command.CreateParameter();
                    pPromo.ParameterName = "@id";
                    pPromo.Value = promocaoID;
                    command.Parameters.Add(pPromo);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateProdutoPromocao(int produtopromocaoID)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    UPDATE 
                        LOG_EXPORT_CLOUD
                    SET 
                        INTEGRADO = 'S'
                    WHERE 
                        TABELA = 'PRODUTO_PROMOCAO'
                    AND 
                        ID_TABELA = @id;";

                    var pPromo = command.CreateParameter();
                    pPromo.ParameterName = "@id";
                    pPromo.Value = produtopromocaoID;
                    command.Parameters.Add(pPromo);

                    int rowsAffected = command.ExecuteNonQuery();

                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateFormaPagamentoStatus(int formaPagamentoId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE 
                        LOG_EXPORT_CLOUD
                        SET INTEGRADO = 'S'
                        WHERE TABELA = 'ECF_TIPO_PAGAMENTO' 
                        AND ID_TABELA = @id;";

                    var pFormaPg = command.CreateParameter();
                    pFormaPg.ParameterName = "@id";
                    pFormaPg.Value = formaPagamentoId;
                    command.Parameters.Add(pFormaPg);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateGrupoPagamentoStatus(int grupoPagamentoId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE 
                        LOG_EXPORT_CLOUD
                        SET INTEGRADO = 'S'
                        WHERE TABELA = 'GRUPO_PAGAMENTO' 
                        AND ID_TABELA = @id;";

                    var pGrupoPg = command.CreateParameter();
                    pGrupoPg.ParameterName = "@id";
                    pGrupoPg.Value = grupoPagamentoId;
                    command.Parameters.Add(pGrupoPg);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdatePdvStatus(int pdvId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE 
                        LOG_EXPORT_CLOUD
                        SET INTEGRADO = 'S'
                        WHERE TABELA = 'TERMINAL_PDV'
                        AND ID_TABELA = @id;";

                    var pPdv = command.CreateParameter();
                    pPdv.ParameterName = "@id";
                    pPdv.Value = pdvId;
                    command.Parameters.Add(pPdv);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateQtdEstoque(int qtdEstoqueId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    UPDATE 
                    LOG_EXPORT_CLOUD
                    SET INTEGRADO = 'S'
                    WHERE TABELA = 'ESTOQUE_PRODUTO'
                    AND ID_TABELA = @id;";

                    var pQtd = command.CreateParameter();
                    pQtd.ParameterName = "@id";
                    pQtd.Value = qtdEstoqueId;
                    command.Parameters.Add(pQtd);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }

        private void UpdateNotaFiscalCabecalhoStatus(int notaFiscalId)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "UPDATE NOTA_FISCAL_CABECALHO SET integrado = 'S' WHERE id = @id";

                    var pNota = command.CreateParameter();
                    pNota.ParameterName = "@id";
                    pNota.Value = notaFiscalId;
                    command.Parameters.Add(pNota);

                    int rowsAffected = command.ExecuteNonQuery();
                    Console.WriteLine($"Rows affected: {rowsAffected}");
                }
            }
        }
        private void UpdateParcelaStatus(int parcelaId, string tabela)
        {
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                UPDATE LOG_EXPORT_REPLI
                SET integrado = 'S'
                WHERE ID_TABELA = @id
                  AND TABELA = @tabela";

                    var pId = command.CreateParameter();
                    pId.ParameterName = "@id";
                    pId.Value = parcelaId;
                    command.Parameters.Add(pId);

                    var pTabela = command.CreateParameter();
                    pTabela.ParameterName = "@tabela";
                    pTabela.Value = tabela;
                    command.Parameters.Add(pTabela);

                    int rowsAffected = command.ExecuteNonQuery();

                    // Log simples (igual você pediu antes)
                    Console.WriteLine($"[UpdateParcelaStatus] ParcelaId={parcelaId} | Tabela={tabela} | Rows={rowsAffected}");
                }
            }
        }


        /*        public async Task<string> PostAsync(string url, object data) //Post retorno JSON
                {
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.DefaultRequestHeaders.Add("Authorization", "Basic_1927b11f4d4186c2f92d04a25956a41ed5c93909b350560e31b8b5719b43");

                        var json = JsonConvert.SerializeObject(data);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");

                        try
                        {
                            var response = await httpClient.PostAsync(url, content);
                            var responseContent = await response.Content.ReadAsStringAsync();

                            // Registrar a resposta completa para depura��o
                            UpdateResponseTextBox($"URL: {url}\nResposta da API: {responseContent}\n");

                            if (!response.IsSuccessStatusCode)
                            {
                                return $"Erro: {response.StatusCode}\nConte�do: {responseContent}";
                            }

                            if (IsJson(responseContent))
                            {
                                return responseContent;
                            }
                            else
                            {
                                return $"Erro: Resposta da API n�o � um JSON v�lido.\nConte�do: {responseContent}";
                            }
                        }
                        catch (Exception ex)
                        {
                            return $"Erro durante a solicita��o: {ex.Message}";
                        }
                    }
                }*/

        private bool IsJson(string input)
        {
            input = input.Trim();
            return (input.StartsWith("{") && input.EndsWith("}")) || // Objeto
                   (input.StartsWith("[") && input.EndsWith("]"));  // Matriz
        }

        private void UpdateResponseTextBox(string responseContent) //UPLOAD EM JSON
        {
            textBoxResponse.Invoke(new Action(() =>
            {
                textBoxResponse.AppendText(responseContent + Environment.NewLine);
                textBoxResponse.ScrollToCaret();
            }));
        }

        /*        private string FormatApiResponse(string responseContent) //FORMATO API JSON
                {
                    dynamic jsonResponse;
                    try
                    {
                        jsonResponse = JsonConvert.DeserializeObject(responseContent);
                    }
                    catch (JsonException)
                    {
                        return $"Erro de formata��o: Resposta n�o � um JSON v�lido.\nConte�do: {responseContent}";
                    }

                    string status = jsonResponse?.status;
                    string message = jsonResponse?.mensagem;
                    return responseContent;
                }*/

        private dynamic[] GetFuncionarioFromDatabase()
        {
            var funcionarios = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    select c.id, c.NOME as descricao,
                    c.SENHA as senha,
                    (case when (select id from CARGO_FUNCIONARIO where ID_FUNCIONARIO=c.id and  ID_CARGO=2) > 0 then '1'else '0'end) as caixa,
                    (case when (select id from CARGO_FUNCIONARIO where ID_FUNCIONARIO=c.id and  ID_CARGO=1) > 0 then '0'else '1'end) as vendedor,
                    c.login
                    from vw_lst_funcionario_empresa c
                    WHERE c.id in (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='ECF_FUNCIONARIO' and INTEGRADO='n')";

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            funcionarios.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("id")),
                                descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? null : reader.GetString(reader.GetOrdinal("descricao")),
                                vendedor = reader.GetString(reader.GetOrdinal("vendedor")),
                                caixa = reader.GetString(reader.GetOrdinal("caixa")),
                                senha = reader.IsDBNull(reader.GetOrdinal("senha")) ? null : reader.GetString(reader.GetOrdinal("senha")),
                                login = reader.IsDBNull(reader.GetOrdinal("login")) ? null : reader.GetString(reader.GetOrdinal("login"))
                            });
                        }
                    }
                }
            }
            return funcionarios.ToArray();
        }

        private dynamic[] GetProdutosFromDatabase() //Produto grupo
        {
            var produtos = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT
                        C.id, c.nome as descricao, COALESCE(c.TAXA_COMISSAO,0) as percentual_comissao
                    FROM 
                        GRUPO_PRODUTO C 
                    WHERE 
                        c.id in (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='GRUPO_PRODUTO' and INTEGRADO='N')";
                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            produtos.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("id")),
                                descricao = reader.GetString(reader.GetOrdinal("descricao")),
                                percentual_comissao = reader.GetDecimal(reader.GetOrdinal("percentual_comissao"))
                            });
                        }
                    }
                }
            }
            return produtos.ToArray();
        }
        private dynamic[] GetMovimentoCaixaFromDatabase()
        {
            var caixas = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT id, descricao,
                           DATA_SANGRIA AS data_movimento, valor,
                           ID_OPERADOR AS funcionario_id,
                           1 AS tipo_movimento_caixa_id,
                           ID_TERMINAL_PDV AS terminal_pdv_id
                    FROM ECF_SANGRIA
                    WHERE id IN (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA = 'ECF_SANGRIA' AND INTEGRADO = 'N')
                    UNION
                    SELECT id, descricao,
                           DATA_SUPRIMENTO AS data_movimento, valor,
                           ID_OPERADOR AS funcionario_id,
                           2 AS tipo_movimento_caixa_id,
                           ID_TERMINAL_PDV AS terminal_pdv_id
                    FROM ECF_SUPRIMENTO
                    WHERE id IN (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA = 'ECF_SUPRIMENTO' AND INTEGRADO = 'N');";
                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            caixas.Add(new
                            {
                                id = reader.IsDBNull(reader.GetOrdinal("id")) ? 0 : reader.GetInt32(reader.GetOrdinal("id")),
                                descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? string.Empty : reader.GetString(reader.GetOrdinal("descricao")),
                                data_movimento = reader.IsDBNull(reader.GetOrdinal("data_movimento")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("data_movimento")),
                                valor = reader.IsDBNull(reader.GetOrdinal("valor")) ? 0 : reader.GetDecimal(reader.GetOrdinal("valor")),
                                funcionario_id = reader.IsDBNull(reader.GetOrdinal("funcionario_id")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("funcionario_id")),
                                tipo_movimento_caixa_id = reader.IsDBNull(reader.GetOrdinal("tipo_movimento_caixa_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("tipo_movimento_caixa_id")),
                                terminal_pdv_id = reader.IsDBNull(reader.GetOrdinal("terminal_pdv_id")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("terminal_pdv_id"))
                            });
                        }
                    }
                }
            }
            return caixas.ToArray();
        }

        private dynamic[] GetProdutosLFromDatabase() //Produto unidade
        {
            var produtosL = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT c.id, LEFT(c.NOME, 50) as descricao, gc.NOME as subgrupo_produto, c.ID_STATUS_PRODUTO,
                    cd.DESCRICAO as marca_produto, ud.NOME as unidade,
                    mr.FANTASIA as fornecedor, c.referencia, COALESCE(c.gtin, '') as gtin,
                    COALESCE(c.VALOR_COMISSAO, 0) as percentual_comissao, COALESCE(c.ESTOQUE_MIN, 0) as quantidade_minima,
                    COALESCE(c.ESTOQUE_MAX, 0) as quantidade_maxima, c.DESCRICAO_STORE as descricao_store, c.PRODUTO_DESTAQUE as produto_destaque,
                    c.id_grupo_produto as grupo_produto_id,
                    c.cest, c.ncm, c.VENDE_STORE,
                    COALESCE((select pp.VALOR_PRODUTO from preco_PRODUTO pp where pp.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and pp.ID_TIPO_PRECO_PRODUTO=1 and pp.ID_PRODUTO=c.id), 0) as preco1,
                    COALESCE((select pp.VALOR_PRODUTO from preco_PRODUTO pp where pp.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and pp.ID_TIPO_PRECO_PRODUTO=2 and pp.ID_PRODUTO=c.id), 0) as preco2,
                    COALESCE((select pp.VALOR_PRODUTO from preco_PRODUTO pp where pp.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and pp.ID_TIPO_PRECO_PRODUTO=3 and pp.ID_PRODUTO=c.id), 0) as preco3,
                    COALESCE((select pp.VALOR_PRODUTO from preco_PRODUTO pp where pp.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and pp.ID_TIPO_PRECO_PRODUTO=4 and pp.ID_PRODUTO=c.id), 0) as preco4,

                    coalesce((select ep.QTD_ESTOQUE from ESTOQUE_PRODUTO ep where ep.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and ep.ID_TIPO_ESTOQUE_PRODUTO=1 and ep.ID_PRODUTO=c.id),0) qtd_estoque,
                    coalesce((select ep.QTD_ESTOQUE from ESTOQUE_PRODUTO ep where ep.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and ep.ID_TIPO_ESTOQUE_PRODUTO=2 and ep.ID_PRODUTO=c.id),0) qtd_estoque2,
                    COALESCE(c.TAXA_COMISSAO,0) as percentual_comissao, ud.PODE_FRACIONAR as pode_fracionar
                    FROM produto C
                    INNER JOIN produto_marca cd ON cd.ID = c.ID_PRODUTO_MARCA
                    INNER JOIN SUBGRUPO_PRODUTO gc ON gc.ID = c.ID_SUBGRUPO_PRODUTO
                    INNER JOIN UNIDADE_PRODUTO ud ON ud.ID = c.ID_UNIDADE_PRODUTO
                    INNER JOIN FORNECEDOR mr ON mr.ID = c.ID_FORNECEDOR
                    and c.id in (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='PRODUTO' and INTEGRADO='N' )";

                    var pEmpresa = command.CreateParameter();
                    pEmpresa.ParameterName = "@idEmpresa_TERMINAL";
                    pEmpresa.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(pEmpresa);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            produtosL.Add(new
                            {
                                id_status_produto = reader.GetInt32(reader.GetOrdinal("ID_STATUS_PRODUTO")),
                                grupo_produto_id = reader.GetInt32(reader.GetOrdinal("grupo_produto_id")),
                                id = reader.GetInt32(reader.GetOrdinal("id")),
                                descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? null : reader.GetString(reader.GetOrdinal("descricao")),
                                subgrupo_produto = reader.IsDBNull(reader.GetOrdinal("subgrupo_produto")) ? null : reader.GetString(reader.GetOrdinal("subgrupo_produto")),
                                marca_produto = reader.IsDBNull(reader.GetOrdinal("marca_produto")) ? null : reader.GetString(reader.GetOrdinal("marca_produto")),
                                unidade = reader.IsDBNull(reader.GetOrdinal("unidade")) ? null : reader.GetString(reader.GetOrdinal("unidade")),
                                fornecedor = reader.IsDBNull(reader.GetOrdinal("fornecedor")) ? null : reader.GetString(reader.GetOrdinal("fornecedor")),
                                referencia = reader.IsDBNull(reader.GetOrdinal("referencia")) ? null : reader.GetString(reader.GetOrdinal("referencia")),
                                gtin = reader.IsDBNull(reader.GetOrdinal("gtin")) ? null : reader.GetString(reader.GetOrdinal("gtin")),
                                percentual_comissao = reader.GetDecimal(reader.GetOrdinal("percentual_comissao")),
                                quantidade_minima = reader.GetDecimal(reader.GetOrdinal("quantidade_minima")),
                                quantidade_maxima = reader.GetDecimal(reader.GetOrdinal("quantidade_maxima")),
                                cest = reader.IsDBNull(reader.GetOrdinal("cest")) ? null : reader.GetString(reader.GetOrdinal("cest")),
                                pode_fracionar = reader.IsDBNull(reader.GetOrdinal("pode_fracionar")) ? null : reader.GetString(reader.GetOrdinal("pode_fracionar")),
                                ncm = reader.IsDBNull(reader.GetOrdinal("ncm")) ? null : reader.GetString(reader.GetOrdinal("ncm")),
                                vende_store = reader.IsDBNull(reader.GetOrdinal("VENDE_STORE")) ? null : reader.GetString(reader.GetOrdinal("VENDE_STORE")),
                                produto_destaque = reader.IsDBNull(reader.GetOrdinal("produto_destaque")) ? null : reader.GetString(reader.GetOrdinal("produto_destaque")),
                                descricao_store = reader.IsDBNull(reader.GetOrdinal("descricao_store")) ? null : reader.GetString(reader.GetOrdinal("descricao_store")),
                                preco1 = reader.GetDecimal(reader.GetOrdinal("preco1")),
                                preco2 = reader.GetDecimal(reader.GetOrdinal("preco2")),
                                qtd_estoque = reader.GetDecimal(reader.GetOrdinal("qtd_estoque")),
                            });
                        }
                    }
                }
            }
            return produtosL.ToArray();
        }

        private dynamic[] GetQtdEstoqueFromDatabase() //Produto unidade
        {
            var qtdEstoques = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    select ID_PRODUTO, QTD_ESTOQUE from estoque_produto e
                    where ID_PRODUTO in (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='ESTOQUE_PRODUTO' and INTEGRADO='N' )
                    and e.ID_ECF_EMPRESA=@idEmpresa_TERMINAL";

                    var pEmpresa = command.CreateParameter();
                    pEmpresa.ParameterName = "@idEmpresa_TERMINAL";
                    pEmpresa.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(pEmpresa);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            qtdEstoques.Add(new
                            {
                                id_produto = reader.GetInt32(reader.GetOrdinal("ID_PRODUTO")),
                                qtd_estoque = reader.GetDecimal(reader.GetOrdinal("QTD_ESTOQUE")),
                            });
                        }
                    }
                }
            }
            return qtdEstoques.ToArray();
        }

        private dynamic[] GetCampanhaPromocaoFromDatabase()
        {
            var promocoes = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
			    SELECT
				     ID, DESCRICAO, DATA_INICIAL, DATA_FINAL,
				     STATUS, ID_EMPRESA, ID_TIPO_PRECO_PRODUTO
			    FROM
				     CAMPANHA_PROMOCAO_PRODUTO
			    WHERE 
			         ID_EMPRESA =@idEmpresa_TERMINAL
                and ID in (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='CAMPANHA_PROMOCAO_PRODUTO' and INTEGRADO='N' )";

                    var pEmpresa = command.CreateParameter();
                    pEmpresa.ParameterName = "@idEmpresa_TERMINAL";
                    pEmpresa.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(pEmpresa);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            promocoes.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("ID")),
                                descricao = reader.IsDBNull(reader.GetOrdinal("DESCRICAO"))
                            ? null
                            : reader.GetString(reader.GetOrdinal("DESCRICAO")),

                                data_inicial = reader.IsDBNull(reader.GetOrdinal("DATA_INICIAL"))
                            ? null
                            : reader.GetDateTime(reader.GetOrdinal("DATA_INICIAL")).ToString("yyyy-MM-dd HH:mm:ss"),

                                data_final = reader.IsDBNull(reader.GetOrdinal("DATA_FINAL"))
                            ? null
                            : reader.GetDateTime(reader.GetOrdinal("DATA_FINAL")).ToString("yyyy-MM-dd HH:mm:ss"),

                                empresa_id = reader.GetInt32(reader.GetOrdinal("ID_EMPRESA")),

                                status = reader.IsDBNull(reader.GetOrdinal("STATUS"))
                            ? null
                            : reader.GetString(reader.GetOrdinal("STATUS")),

                                id_tipo_preco_produto = reader.IsDBNull(reader.GetOrdinal("ID_TIPO_PRECO_PRODUTO"))
                            ? (int?)null
                            : reader.GetInt32(reader.GetOrdinal("ID_TIPO_PRECO_PRODUTO"))
                            });
                        }
                    }
                }
            }
            return promocoes.ToArray();
        }

        private dynamic[] GetProdutoPromocaoFromDatabase()
        {
            var produtopromos = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT PP.ID,
                        COALESCE(PP.QUANTIDADE_EM_PROMOCAO, 0) AS QUANTIDADE_EM_PROMOCAO, 
                        COALESCE(PP.QUANTIDADE_MAXIMA_CLIENTE, 0) AS QUANTIDADE_MAXIMA_CLIENTE,
                        PP.VALOR, 
                        COALESCE(PP.qtdja_vendida, 0) AS qtda_vendida, 
				        COALESCE(pp.ID_TIPO_ESTOQUE_PRODUTO,0) as id_tipo_estoque_produto,
                        pd.VALOR_PRODUTO AS VALOR_VENDA, pd.ID_PRODUTO,C.ID as id_campanha_promo_prod
                    FROM PRODUTO_PROMOCAO PP
                    INNER JOIN CAMPANHA_PROMOCAO_PRODUTO C ON C.ID = PP.ID_CAMPANHA_PROMO_PROD
                    INNER JOIN PRECO_PRODUTO pd ON pd.ID_PRODUTO = PP.ID_PRODUTO 
                        AND pd.ID_ECF_EMPRESA = C.id_empresa 
                        AND pd.ID_TIPO_PRECO_PRODUTO = C.ID_TIPO_PRECO_PRODUTO
                    WHERE 
                        C.id_empresa = @idEmpresa_TERMINAL
                    and PP.ID in (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='PRODUTO_PROMOCAO' and INTEGRADO='N' )";

                    var pEmpresa = command.CreateParameter();
                    pEmpresa.ParameterName = "@idEmpresa_TERMINAL";
                    pEmpresa.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(pEmpresa);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            produtopromos.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("ID")),
                                produto_id = reader.GetInt32(reader.GetOrdinal("ID_PRODUTO")),
                                quantidade_em_promocao = reader.GetDecimal(reader.GetOrdinal("QUANTIDADE_EM_PROMOCAO")),
                                quantidade_maxima_cliente = reader.GetDecimal(reader.GetOrdinal("QUANTIDADE_MAXIMA_CLIENTE")),
                                valor = reader.GetDecimal(reader.GetOrdinal("VALOR")),
                                id_campanha_promo_prod = reader.GetInt32(reader.GetOrdinal("id_campanha_promo_prod")),
                                qtda_vendida = reader.GetDecimal(reader.GetOrdinal("qtda_vendida")),
                                id_tipo_estoque_produto = reader.GetInt32(reader.GetOrdinal("id_tipo_estoque_produto")),
                                valor_venda = reader.GetDecimal(reader.GetOrdinal("VALOR_VENDA")),
                            });
                        }
                    }
                }
            }
            return produtopromos.ToArray();
        }

        private dynamic[] GetClientesFromDatabase()
        {
            var clientes = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                DbCommand command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT c.id, c.nome AS razao_social, c.cpf_cnpj AS cnpj, c.fantasia AS nome_fantasia,
                    gc.NOME AS grupo_cliente, cd.NOMECIDADE AS cidade, c.bairro, c.complemento, c.data_nascimento
                    FROM cliente c
                    INNER JOIN cidade cd ON cd.IDCIDADE = c.ID_CIDADE
                    INNER JOIN GRUPO_CLIENTE gc ON gc.id = c.ID_GRUPO_CLIENTE
                    WHERE c.id in (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='Cliente' and INTEGRADO='N')
                    ";

                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        clientes.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            razao_social = reader.IsDBNull(reader.GetOrdinal("razao_social")) ? null : reader.GetString(reader.GetOrdinal("razao_social")),
                            cnpj = reader.IsDBNull(reader.GetOrdinal("cnpj")) ? null : reader.GetString(reader.GetOrdinal("cnpj")),
                            nome_fantasia = reader.IsDBNull(reader.GetOrdinal("nome_fantasia")) ? null : reader.GetString(reader.GetOrdinal("nome_fantasia")),
                            grupo_cliente = reader.IsDBNull(reader.GetOrdinal("grupo_cliente")) ? null : reader.GetString(reader.GetOrdinal("grupo_cliente")),
                            complemento = reader.IsDBNull(reader.GetOrdinal("complemento")) ? null : reader.GetString(reader.GetOrdinal("complemento")),
                            data_nascimento = reader.IsDBNull(reader.GetOrdinal("data_nascimento")) ? null : reader.GetDateTime(reader.GetOrdinal("data_nascimento")).ToString("yyyy-MM-dd"),
                            // vende_store = reader.IsDBNull(reader.GetOrdinal("vende_store")) ? null : reader.GetString(reader.GetOrdinal("vende_store")),
                            cidade = reader.IsDBNull(reader.GetOrdinal("cidade")) ? null : reader.GetString(reader.GetOrdinal("cidade")),
                            bairro = reader.IsDBNull(reader.GetOrdinal("bairro")) ? null : reader.GetString(reader.GetOrdinal("bairro"))
                        });
                    }
                }
            }

            return clientes.ToArray();
        }
        private dynamic[] GetFormaPagamentoFromDatabase()
        {
            var formaPagamento = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                DbCommand command = connection.CreateCommand();
                command.CommandText = @"
                select c.id, c.DESCRICAO as descricao, c.ID_GRUPO_PAGAMENTO as grupo_pagamento_id,
                COALESCE(c.TAXA_COMISSAO,0) as percentual_comissao
                from ECF_TIPO_PAGAMENTO c
                WHERE c.id in (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='ECF_TIPO_PAGAMENTO' and INTEGRADO='n')
                ";

                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        formaPagamento.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? null : reader.GetString(reader.GetOrdinal("descricao")),
                            percentual_comissao = reader.GetDecimal(reader.GetOrdinal("percentual_comissao")),
                            grupo_pagamento_id = reader.GetInt32(reader.GetOrdinal("grupo_pagamento_id"))
                        });
                    }
                }
            }

            return formaPagamento.ToArray();
        }

        private dynamic[] GetGrupoPagamentoFromDatabase()
        {
            var grupoPagamento = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                DbCommand command = connection.CreateCommand();
                command.CommandText = @"
                select ID, DESCRICAO from GRUPO_PAGAMENTO";

                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        grupoPagamento.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("ID")),
                            descricao = reader.IsDBNull(reader.GetOrdinal("DESCRICAO")) ? null : reader.GetString(reader.GetOrdinal("DESCRICAO"))
                        });
                    }
                }
            }

            return grupoPagamento.ToArray();
        }

        private dynamic[] GetPdvFromDatabase()
        {
            var pdvs = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                DbCommand command = connection.CreateCommand();
                command.CommandText = @"
                select c.id, c.DESCRICAO as descricao
                from TERMINAL_PDV c
                WHERE c.id in
                (SELECT ID_TABELA FROM LOG_EXPORT_CLOUD WHERE TABELA='TERMINAL_PDV' and INTEGRADO='N')
                ";

                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        pdvs.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? null : reader.GetString(reader.GetOrdinal("descricao")),
                        });
                    }
                }
            }

            return pdvs.ToArray();
        }


        private dynamic[] GetNotaFiscalCabecalhoFromDatabase()
        {
            var notasFiscais = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                /*                // ETAPA 1: Buscar os par�metros de diret�rio PRIMEIRO
                                string queryParametros = @"
                            SELECT DIRETORIO_ENVIORESPOSTA, DIRETORIO_ENVIORESPOSTANFE
                            FROM PARAMETRO_NFCE
                            WHERE ID_ECF_EMPRESA = @idEmpresa_TERMINAL";

                                using (var commandParam = new SqlCommand(queryParametros, connection))
                                {
                                    commandParam.Parameters.AddWithValue("@idEmpresa_TERMINAL", idEmpresa_TERMINAL);
                                    using (var paramReader = commandParam.ExecuteReader())
                                    {
                                        if (paramReader.Read()) // L� a �nica linha de par�metros
                                        {
                                            diretorioNFCE = paramReader["DIRETORIO_ENVIORESPOSTA"] as string ?? "";
                                            diretorioNFE = paramReader["DIRETORIO_ENVIORESPOSTANFE"] as string ?? "";
                                        }
                                    } // O paramReader � fechado aqui
                                }*/

                // ETAPA 2: Buscar as notas fiscais e usar os par�metros j� salvos
                string queryNotasFiscais = @"
            SELECT 
                c.id, COALESCE(c.OBS_ADICIONAL, '') AS observacao, c.ID_TIPO_NOTA_FISCAL AS tipo_nota_fiscal_id,
                c.ID_CLIENTE AS cliente_id, COALESCE(c.ID_TERMINAL_PDV, 1) AS terminal_pdv_id,
                COALESCE(c.ID_SITUACAO, 0) AS situacao_movimentacao_id,
                COALESCE(c.DATA_AUTORIZACAONF, c.DATA_EMISSAO) AS data_autorizacao, c.DATA_EMISSAO AS data_venda,
                COALESCE(c.NOME_CLIENTE, '') AS nome_cliente, COALESCE(c.NUMERO, 0) AS numero,
                COALESCE(c.SERIE, '') AS serie, c.TOTAL_PRODUTOS, c.TOTAL_NF,
                COALESCE(c.TOTALSERVICOS, 0) AS total_servicos, COALESCE(c.ACRESCIMO, 0) AS total_acrescimo,
                COALESCE(c.DESCONTO, 0) AS total_desconto, c.TOTAL_NF AS total_geral,
                COALESCE(c.ICMS, 0) AS valor_icms, COALESCE(c.PIS, 0) AS valor_pis,
                COALESCE(c.COFINS, 0) AS valor_cofins, COALESCE(c.IPI, 0) AS valor_ipi,
                COALESCE(c.ISSQN, 0) AS valor_iss, COALESCE(c.ID_VENDEDOR, 0) AS vendedor_id,
                COALESCE(c.ID_ECF_FUNCIONARIO, 0) AS caixa_id, COALESCE(c.VALOR_RECEBIDO, 0) AS valor_recebido,
                c.CHAVENF, COALESCE(c.ID_SITUACAO_NFE, 0) AS ID_SITUACAO_NFE,
                COALESCE(c.ID_STATUSNFCE, 0) AS ID_STATUSNFCE
            FROM NOTA_FISCAL_CABECALHO c 
            WHERE 
                c.ID_TIPO_NOTA_FISCAL IN (1, 2, 3, 4, 5, 7, 8, 9)
                AND c.ID_SITUACAO IN (8, 9)
                AND c.INTEGRADO = 'N' 
                AND c.id_empresa = @idEmpresa_TERMINAL";
                //, COALESCE(c.XML_NUVEM, '') AS XML_NUVEM
                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = queryNotasFiscais;

                    DbParameter param = command.CreateParameter();
                    param.ParameterName = "@idEmpresa_TERMINAL";
                    param.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(param);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            notasFiscais.Add(new
                            {
                                id = (int)reader["id"],
                                observacao = (string)reader["observacao"],
                                tipo_nota_fiscal_id = (int)reader["tipo_nota_fiscal_id"],
                                cliente_id = (int)reader["cliente_id"],
                                terminal_pdv_id = (int)reader["terminal_pdv_id"],
                                situacao_movimentacao_id = (int)reader["situacao_movimentacao_id"],
                                data_autorizacao = (DateTime)reader["data_autorizacao"],
                                data_venda = (DateTime)reader["data_venda"],
                                nome_cliente = (string)reader["nome_cliente"],
                                numero = (int)reader["numero"],
                                serie = (string)reader["serie"],
                                total_nf = (decimal)reader["total_nf"],
                                total_produtos = (decimal)reader["TOTAL_PRODUTOS"],
                                total_servicos = (decimal)reader["total_servicos"],
                                total_acrescimo = (decimal)reader["total_acrescimo"],
                                total_desconto = (decimal)reader["total_desconto"],
                                total_geral = (decimal)reader["total_geral"],
                                valor_icms = (decimal)reader["valor_icms"],
                                valor_pis = (decimal)reader["valor_pis"],
                                valor_cofins = (decimal)reader["valor_cofins"],
                                valor_ipi = (decimal)reader["valor_ipi"],
                                valor_iss = (decimal)reader["valor_iss"],
                                vendedor_id = (int)reader["vendedor_id"],
                                caixa_id = (int)reader["caixa_id"],
                                valor_recebido = (decimal)reader["valor_recebido"],
                                //chave_acesso = (string)reader["CHAVENF"],
                                idSituacaoNF = (int)reader["ID_SITUACAO_NFE"],
                                idStatusNFCe = (int)reader["ID_STATUSNFCE"]
                                // = (string)["XML_NUVEM"],
                                // Atribui os valores das vari�veis a cada nota
                                // caminhoNFE = diretorioNFE,
                                // caminhoNFCE = diretorioNFCE
                            });
                        }
                    }
                }
            }
            return notasFiscais.ToArray();
        }

        private dynamic[] GetCabecalhoParcela2()
        {
            var parcelas = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                SELECT
                    v.ID, v.HISTORICO, v.ID_TIPO_PAGAMENTO, v.ID_PLANO_CONTA, 
                    v.ID_PESSOA, v.TIPO, v.NUMERO_DOCUMENTO,
                    v.VALOR_TOTAL, v.DATA_LANCAMENTO, v.PRIMEIRO_VENCIMENTO, 
                    v.QUANTIDADE_PARCELA, v.ID_MOVIMENTACAO,
                    v.ID_EMPRESA, v.INTERVALO_VENCIMENTO, v.ID_CENTRO_CUSTO, 
                    v.STATUS_PREVISAO, v.FIXA_VENCIMENTO,
                    v.ID_TERMINAL_PDV, v.BOLETO_IMPRESSO, 
                    v.STBAIXA_RETORNO, v.STATUS_ESTORNO
                FROM CONTAS_PARCELAS v
                INNER JOIN CONTAS_DETALHE cd 
                    ON cd.ID_CONTAS_PARCELAS = v.ID
                WHERE 
                    v.ID_EMPRESA = @idEmpresa
                    AND cd.ID_SITUACAO_PARCELA IN (1, 2)
                    AND v.DATA_LANCAMENTO >= @dataInicial";

                    // parâmetro empresa
                    var pEmpresa = command.CreateParameter();
                    pEmpresa.ParameterName = "@idEmpresa";
                    pEmpresa.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(pEmpresa);

                    // parâmetro data
                    var pData = command.CreateParameter();
                    pData.ParameterName = "@dataInicial";
                    pData.Value = new DateTime(2025, 6, 1);
                    pData.DbType = DbType.DateTime;
                    command.Parameters.Add(pData);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            parcelas.Add(new
                            {
                                id = reader["ID"] != DBNull.Value ? Convert.ToInt32(reader["ID"]) : 0,
                                idOriginal = reader["ID"] != DBNull.Value ? Convert.ToInt32(reader["ID"]) : 0,
                                historico = reader["HISTORICO"]?.ToString(),
                                id_tipo_pagamento = reader["ID_TIPO_PAGAMENTO"] != DBNull.Value ? Convert.ToInt32(reader["ID_TIPO_PAGAMENTO"]) : (int?)null,
                                id_plano_conta = reader["ID_PLANO_CONTA"] != DBNull.Value ? Convert.ToInt32(reader["ID_PLANO_CONTA"]) : 0,
                                id_pessoa = reader["ID_PESSOA"] != DBNull.Value ? Convert.ToInt32(reader["ID_PESSOA"]) : 0,
                                tipo = reader["TIPO"]?.ToString(),
                                numero_documento = reader["NUMERO_DOCUMENTO"]?.ToString(),
                                valor_total = reader["VALOR_TOTAL"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR_TOTAL"]) : 0m,
                                data_lancamento = reader["DATA_LANCAMENTO"] != DBNull.Value ? Convert.ToDateTime(reader["DATA_LANCAMENTO"]) : (DateTime?)null,
                                primeiro_vencimento = reader["PRIMEIRO_VENCIMENTO"] != DBNull.Value ? Convert.ToDateTime(reader["PRIMEIRO_VENCIMENTO"]) : (DateTime?)null,
                                quantidade_parcela = reader["QUANTIDADE_PARCELA"] != DBNull.Value ? Convert.ToInt32(reader["QUANTIDADE_PARCELA"]) : (int?)null,
                                id_movimentacao = reader["ID_MOVIMENTACAO"] != DBNull.Value ? Convert.ToInt32(reader["ID_MOVIMENTACAO"]) : 0,
                                id_empresa = reader["ID_EMPRESA"] != DBNull.Value ? Convert.ToInt32(reader["ID_EMPRESA"]) : 0,
                                intervalo_vencimento = reader["INTERVALO_VENCIMENTO"] != DBNull.Value ? Convert.ToInt32(reader["INTERVALO_VENCIMENTO"]) : (int?)null,
                                id_centro_custo = reader["ID_CENTRO_CUSTO"] != DBNull.Value ? Convert.ToInt32(reader["ID_CENTRO_CUSTO"]) : 0,
                                status_previsao = reader["STATUS_PREVISAO"]?.ToString(),
                                fixa_vencimento = reader["FIXA_VENCIMENTO"]?.ToString(),
                                id_terminal_pdv = reader["ID_TERMINAL_PDV"] != DBNull.Value ? Convert.ToInt32(reader["ID_TERMINAL_PDV"]) : 0,
                                boleto_impresso = reader["BOLETO_IMPRESSO"]?.ToString(),
                                stbaixa_retorno = reader["STBAIXA_RETORNO"]?.ToString(),
                                status_estorno = reader["STATUS_ESTORNO"]?.ToString()
                            });
                        }
                    }
                }
            }

            return parcelas.ToArray();
        }
        private List<DetalheParcela> GetParcelasDetalhes(int parcelaID)
        {
            var detalhes = new List<DetalheParcela>();


            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT *
                    FROM CONTAS_DETALHE
                    WHERE ID_CONTAS_PARCELAS = @parcelaID";

                    var pParcela = command.CreateParameter();
                    pParcela.ParameterName = "@parcelaID";
                    pParcela.Value = parcelaID;
                    command.Parameters.Add(pParcela);
                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            detalhes.Add(new DetalheParcela
                            {
                                id = reader["ID"] != DBNull.Value ? Convert.ToInt32(reader["ID"]) : 0,
                                id_contas_parcelas = reader["ID_CONTAS_PARCELAS"] != DBNull.Value ? Convert.ToInt32(reader["ID_CONTAS_PARCELAS"]) : 0,
                                data_vencimento = reader["DATA_VENCIMENTO"] != DBNull.Value ? Convert.ToDateTime(reader["DATA_VENCIMENTO"]) : (DateTime?)null,
                                data_pagamento = reader["DATA_PAGAMENTO"] != DBNull.Value ? Convert.ToDateTime(reader["DATA_PAGAMENTO"]) : (DateTime?)null,
                                numero_parcela = reader["NUMERO_PARCELA"] != DBNull.Value ? Convert.ToInt32(reader["NUMERO_PARCELA"]) : (int?)null,
                                valor = reader["VALOR"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR"]) : 0m,
                                taxa_juros = reader["TAXA_JUROS"] != DBNull.Value ? Convert.ToDecimal(reader["TAXA_JUROS"]) : 0m,
                                taxa_multa = reader["TAXA_MULTA"] != DBNull.Value ? Convert.ToDecimal(reader["TAXA_MULTA"]) : 0m,
                                taxa_desconto = reader["TAXA_DESCONTO"] != DBNull.Value ? Convert.ToDecimal(reader["TAXA_DESCONTO"]) : 0m,
                                valor_juros = reader["VALOR_JUROS"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR_JUROS"]) : 0m,
                                id_situacao_parcela = reader["ID_SITUACAO_PARCELA"] != DBNull.Value ? Convert.ToInt32(reader["ID_SITUACAO_PARCELA"]) : 0,
                                valor_pago = reader["VALOR_PAGO"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR_PAGO"]) : 0m,
                                id_empresa_pago = reader["ID_EMPRESA_PAGO"] != DBNull.Value ? Convert.ToInt32(reader["ID_EMPRESA_PAGO"]) : 0,
                                boleto_impresso = reader["BOLETO_IMPRESSO"] as string,
                                stbaixa_retorno = reader["STBAIXA_RETORNO"] as string,
                                nosso_numero = reader["NOSSO_NUMERO"] != DBNull.Value ? Convert.ToInt32(reader["NOSSO_NUMERO"]) : 0,
                                status_estorno = reader["STATUS_ESTORNO"] as string,
                                status_previsao = reader["STATUS_PREVISAO"] as string
                            });
                        }
                    }
                }
            }
            return detalhes;
        }
        private List<DetalheParcela> GetParcelasDetalhes2()
        {
            var detalhes = new List<DetalheParcela>();
            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT *
                    FROM CONTAS_DETALHE cd
                    INNER JOIN LOG_EXPORT_REPLI v ON v.ID_TABELA = cd.ID
                    WHERE v.INTEGRADO = 'N' and v.TABELA = 'CONTAS_DETALHE'";

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            detalhes.Add(new DetalheParcela
                            {
                                id = reader["ID"] != DBNull.Value ? Convert.ToInt32(reader["ID"]) : 0,
                                id_contas_parcelas = reader["ID_CONTAS_PARCELAS"] != DBNull.Value ? Convert.ToInt32(reader["ID_CONTAS_PARCELAS"]) : 0,
                                data_vencimento = reader["DATA_VENCIMENTO"] != DBNull.Value ? Convert.ToDateTime(reader["DATA_VENCIMENTO"]) : (DateTime?)null,
                                data_pagamento = reader["DATA_PAGAMENTO"] != DBNull.Value ? Convert.ToDateTime(reader["DATA_PAGAMENTO"]) : (DateTime?)null,
                                numero_parcela = reader["NUMERO_PARCELA"] != DBNull.Value ? Convert.ToInt32(reader["NUMERO_PARCELA"]) : (int?)null,
                                valor = reader["VALOR"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR"]) : 0m,
                                taxa_juros = reader["TAXA_JUROS"] != DBNull.Value ? Convert.ToDecimal(reader["TAXA_JUROS"]) : 0m,
                                taxa_multa = reader["TAXA_MULTA"] != DBNull.Value ? Convert.ToDecimal(reader["TAXA_MULTA"]) : 0m,
                                taxa_desconto = reader["TAXA_DESCONTO"] != DBNull.Value ? Convert.ToDecimal(reader["TAXA_DESCONTO"]) : 0m,
                                valor_juros = reader["VALOR_JUROS"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR_JUROS"]) : 0m,
                                id_situacao_parcela = reader["ID_SITUACAO_PARCELA"] != DBNull.Value ? Convert.ToInt32(reader["ID_SITUACAO_PARCELA"]) : 0,
                                valor_pago = reader["VALOR_PAGO"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR_PAGO"]) : 0m,
                                id_empresa_pago = reader["ID_EMPRESA_PAGO"] != DBNull.Value ? Convert.ToInt32(reader["ID_EMPRESA_PAGO"]) : 0,
                                boleto_impresso = reader["BOLETO_IMPRESSO"] as string,
                                stbaixa_retorno = reader["STBAIXA_RETORNO"] as string,
                                nosso_numero = reader["NOSSO_NUMERO"] != DBNull.Value ? Convert.ToInt32(reader["NOSSO_NUMERO"]) : 0,
                                status_estorno = reader["STATUS_ESTORNO"] as string,
                                status_previsao = reader["STATUS_PREVISAO"] as string
                            });
                        }
                    }
                }
            }
            return detalhes;
        }
        private List<PagamentoParcela> GetParcelasPagamentos(int detalheId)
        {
            var pagamentos = new List<PagamentoParcela>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT cp.*
                    FROM CONTAS_PAGAMENTO cp
                    INNER JOIN CONTAS_DETALHE cd ON cd.ID = cp.ID_CONTAS_DETALHES
                    WHERE cd.ID_CONTAS_PARCELAS = @detalheId";

                    var pDetalhe = command.CreateParameter();
                    pDetalhe.ParameterName = "@detalheId";
                    pDetalhe.Value = detalheId;
                    command.Parameters.Add(pDetalhe);
                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            pagamentos.Add(new PagamentoParcela
                            {
                                id = reader["ID"] != DBNull.Value ? Convert.ToInt32(reader["ID"]) : 0,
                                id_contas_detalhes = reader["ID_CONTAS_DETALHES"] != DBNull.Value ? Convert.ToInt32(reader["ID_CONTAS_DETALHES"]) : 0,
                                id_ecf_tipo_pagamento = reader["ID_ECF_TIPO_PAGAMENTO"] != DBNull.Value ? Convert.ToInt32(reader["ID_ECF_TIPO_PAGAMENTO"]) : 0,
                                valor_pagamento = reader["VALOR_PAGAMENTO"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR_PAGAMENTO"]) : 0m,
                                data_estorno = reader["DATA_ESTORNO"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["DATA_ESTORNO"]) : null,
                                status_estorno = reader["STATUS_ESTORNO"] as string,
                                id_original = reader["ID_ORIGINAL"] != DBNull.Value ? (int?)Convert.ToInt32(reader["ID_ORIGINAL"]) : null,
                                id_terminal_pdv = reader["ID_TERMINAL_PDV"] != DBNull.Value ? Convert.ToInt32(reader["ID_TERMINAL_PDV"]) : 0,
                                tipo_baixa = reader["TIPO_BAIXA"] as string,
                                id_cpag_terminal = reader["ID_CPAG_TERMINAL"] != DBNull.Value ? (int?)Convert.ToInt32(reader["ID_CPAG_TERMINAL"]) : null,
                                id_conta_bancaria = reader["ID_CONTA_BANCARIA"] != DBNull.Value ? Convert.ToInt32(reader["ID_CONTA_BANCARIA"]) : 0
                            });
                        }
                    }
                }
            }
            return pagamentos;
        }
        private List<PagamentoParcela> GetParcelasPagamentos2()
        {
            var pagamentos = new List<PagamentoParcela>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT cp.*
                    FROM CONTAS_PAGAMENTO cp
                    INNER JOIN LOG_EXPORT_REPLI v ON v.ID_TABELA = cp.ID
                    WHERE v.INTEGRADO = 'N' and v.TABELA = 'CONTAS_PAGAMENTO'";

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            pagamentos.Add(new PagamentoParcela
                            {
                                id = reader["ID"] != DBNull.Value ? Convert.ToInt32(reader["ID"]) : 0,
                                id_contas_detalhes = reader["ID_CONTAS_DETALHES"] != DBNull.Value ? Convert.ToInt32(reader["ID_CONTAS_DETALHES"]) : 0,
                                id_ecf_tipo_pagamento = reader["ID_ECF_TIPO_PAGAMENTO"] != DBNull.Value ? Convert.ToInt32(reader["ID_ECF_TIPO_PAGAMENTO"]) : 0,
                                valor_pagamento = reader["VALOR_PAGAMENTO"] != DBNull.Value ? Convert.ToDecimal(reader["VALOR_PAGAMENTO"]) : 0m,
                                data_estorno = reader["DATA_ESTORNO"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(reader["DATA_ESTORNO"]) : null,
                                status_estorno = reader["STATUS_ESTORNO"] as string,
                                id_original = reader["ID_ORIGINAL"] != DBNull.Value ? (int?)Convert.ToInt32(reader["ID_ORIGINAL"]) : null,
                                id_terminal_pdv = reader["ID_TERMINAL_PDV"] != DBNull.Value ? Convert.ToInt32(reader["ID_TERMINAL_PDV"]) : 0,
                                tipo_baixa = reader["TIPO_BAIXA"] as string,
                                id_cpag_terminal = reader["ID_CPAG_TERMINAL"] != DBNull.Value ? (int?)Convert.ToInt32(reader["ID_CPAG_TERMINAL"]) : null,
                                id_conta_bancaria = reader["ID_CONTA_BANCARIA"] != DBNull.Value ? Convert.ToInt32(reader["ID_CONTA_BANCARIA"]) : 0
                            });
                        }
                    }
                }
            }
            return pagamentos;
        }


        private dynamic[] GetNotaFiscalDetalhesFromDatabase(int notaFiscalId)
        {
            var itensDetalhes = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT 
                      d.id,
                        COALESCE(d.ID_PRODUTO,1) AS produto_id,
                        COALESCE(d.VALOR_UNITARIO,0) as valor_unitario,
                        COALESCE(d.CUSTO_PROD, 0) AS valor_custo,
                        COALESCE(d.quantidade,0) as quantidade,
                        COALESCE(d.VALOR_PRODUTOS,0) AS sub_total,
                        COALESCE(d.desconto, 0) AS desconto,
                        COALESCE(d.acrescimo,0) as acrescimo,
                        COALESCE(d.VALOR_TOTAL,0) as valor_total,
                        COALESCE(d.icms, 0) AS valor_pis,
	                    COALESCE(d.pis, 0) AS valor_cofins,
	                    COALESCE(d.cofins, 0) AS valor_ipi,
	                    COALESCE(d.issqn, 0) AS valor_icms,
	                    COALESCE(d.ipi, 0) AS valor_iss,
                        COALESCE(d.cst, 00) AS id_cst_icms,
                        COALESCE(d.CST_PIS, 99) AS id_cst_pis,
                        COALESCE(d.CST_COFINS, 1) AS id_cst_cofins,
                        COALESCE(d.CST_IPI, 00) AS id_cst_ipi,
	                    d.NOME_PRODUTO
                    FROM NOTA_FISCAL_DETALHE d 
                    WHERE d.ID_NF_CABECALHO = @notaFiscalId";

                    var pNota = command.CreateParameter();
                    pNota.ParameterName = "@notaFiscalId";
                    pNota.Value = notaFiscalId;
                    command.Parameters.Add(pNota);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            itensDetalhes.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("id")),
                                produto_id = reader.GetInt32(reader.GetOrdinal("produto_id")),
                                valor_unitario = reader.GetDecimal(reader.GetOrdinal("valor_unitario")),
                                nome_produto = reader.GetString(reader.GetOrdinal("nome_produto")),
                                valor_custo = reader.GetDecimal(reader.GetOrdinal("valor_custo")),
                                quantidade = reader.GetDecimal(reader.GetOrdinal("quantidade")),
                                sub_total = reader.GetDecimal(reader.GetOrdinal("sub_total")),
                                desconto = reader.GetDecimal(reader.GetOrdinal("desconto")),
                                acrescimo = reader.GetDecimal(reader.GetOrdinal("acrescimo")),
                                valor_total = reader.GetDecimal(reader.GetOrdinal("valor_total")),
                                valor_icms = reader.GetDecimal(reader.GetOrdinal("valor_icms")),
                                valor_pis = reader.GetDecimal(reader.GetOrdinal("valor_pis")),
                                valor_cofins = reader.GetDecimal(reader.GetOrdinal("valor_cofins")),
                                valor_ipi = reader.GetDecimal(reader.GetOrdinal("valor_ipi")),
                                valor_iss = reader.GetDecimal(reader.GetOrdinal("valor_iss")),
                                id_cst_icms = reader.GetInt32(reader.GetOrdinal("id_cst_icms")),
                                id_cst_pis = reader.GetInt32(reader.GetOrdinal("id_cst_pis")),
                                id_cst_cofins = reader.GetInt32(reader.GetOrdinal("id_cst_cofins")),
                                id_cst_ipi = reader.GetInt32(reader.GetOrdinal("id_cst_ipi"))
                            });
                        }
                    }
                }
            }
            return itensDetalhes.ToArray();
        }

        private dynamic[] GetNotaFiscalPagamentoFromDatabase(int notaFiscalId)
        {
            var pagamentos = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT 
                        p.id,
                        COALESCE(p.ID_ECF_TIPO_PAGAMENTO, 0) AS forma_pagamento_id,
                        COALESCE(p.valor, 0) AS valor
                    FROM NOTA_FISCAL_TIPO_PAGAMENTO p
                    WHERE p.ID_NF_CABECALHO = @notaFiscalId";

                    // Adding the parameter for the query

                    var pNota = command.CreateParameter();
                    pNota.ParameterName = "@notaFiscalId";
                    pNota.Value = notaFiscalId;
                    command.Parameters.Add(pNota);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            pagamentos.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("id")),
                                forma_pagamento_id = reader.GetInt32(reader.GetOrdinal("forma_pagamento_id")),
                                valor = reader.GetDecimal(reader.GetOrdinal("valor"))
                            });
                        }
                    }
                }
            }
            return pagamentos.ToArray();
        }
        private string FormatApiResponse(string responseContent, [CallerMemberName] string callerName = "")
        {
            try
            {
                var formatted = JObject.Parse(responseContent);
                var status = formatted["status"]?.ToString();
                var resultado = formatted["data"]?["resultado"]?.ToString();

                if (status == "success" && resultado == "ok")
                {
                    return $"{callerName}: Aguarde, realizando upload.";
                }
                else
                {
                    return $"{callerName}: Aguarde, realizando upload.";
                }
            }
            catch (Exception)
            {
                return $"{callerName}: ERRO de conex�o, estamos reconectando";
            }
        }

        private void AppendTextWithTimestamp(string message)
        {
            textBoxResponse.AppendText($"{message}");
            textBoxResponse.SelectionStart = textBoxResponse.Text.Length;
            textBoxResponse.ScrollToCaret(); // Garante que a rolagem esteja na �ltima linha
        }
        public async Task<string> PostAsync(string url, object data, int maxRetries = 3)
        {
            int attempt = 0;

            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    using var client = new HttpClient();
                    var request = new HttpRequestMessage(HttpMethod.Post, url);

                    // Header exatamente como funcionava antes
                    request.Headers.Add("Authorization", "Basic_1927b11f4d4186c2f92d04a25956a41ed5c93909b350560e31b8b5719b43");

                    string json = JsonConvert.SerializeObject(data);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.SendAsync(request);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    WriteLogToFile($"[POST {url}] Tentativa {attempt}, StatusCode: {response.StatusCode}, Retorno: {responseContent}");

                    response.EnsureSuccessStatusCode(); // Lan�a exce��o se n�o for 2xx
                    return responseContent;
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    WriteLogToFile($"Timeout na tentativa {attempt}: {ex.Message}. Retentando em 10s...");
                    await Task.Delay(10000);
                }
                catch (Exception ex)
                {
                    WriteLogToFile($"Erro na tentativa {attempt}: {ex.Message}");
                    if (attempt >= maxRetries)
                        return $"Erro ap�s {attempt} tentativas: {ex.Message}";
                    await Task.Delay(5000); // Pequena espera antes de tentar novamente
                }
            }

            return "Erro desconhecido ap�s v�rias tentativas";
        }

        // CODIGO DO ENVIO INICIAL
        private async void button1EnvioInicial_Click(object sender, EventArgs e)
        {
            await ExecuteSendMethods2();

            // Atualiza o estado dos bot�es ap�s o envio inicial
            UpdateButtonStatesAfterInitialSend();

            // Cria um arquivo para indicar que o envio inicial foi feito
            File.WriteAllText(initialSendConfigFilePath, "InitialSendDone");
        }

        private async Task ExecuteSendMethods2()
        {
            textBoxResponse.Clear();

            try
            {
                var apiData = idEmpresa_API;

                var responseMessages = new List<string>();

                UpdateResponseTextBox("Aguarde, realizando upload de Clientes...");
                await SendClientes2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Clientes");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Formas de Pagamento...");
                await SendFormaPagamento2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Formas de Pagamento");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Grupo de Pagamento...");
                await SendGrupoPagamento2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Formas de Pagamento");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Funcion�rios...");
                await SendFuncionarios2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Funcion�rios");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Promo��es...");
                await SendCampanhaPromocao2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Promo��es");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Promo��es de Promo��es...");
                await SendProdutoPromocao2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Promo��es");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de PDV...");
                await SendPdv2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para PDV");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Grupo de Produtos...");
                await SendProdutos2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Grupo de Produtos");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Produtos...");
                await SendProdutosL2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Produtos");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Movimento de Caixa...");
                await SendMovimentoCaixa2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Movimento de Caixa");
                textBoxResponse.Clear();

                UpdateResponseTextBox("Aguarde, realizando upload de Nota Fiscal...");
                await SendNotaFiscalCabecalho2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Nota Fiscal");
                textBoxResponse.Clear();

/*                UpdateResponseTextBox("Aguarde, realizando upload de Parcelas...");
                await SendParcelas2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Parcelas");*/

                // Mensagem final indicando que todos os uploads foram realizados
                UpdateResponseTextBox("Todos os Uploads Realizados");
            }
            catch (Exception ex)
            {
                // Atualiza a resposta com a mensagem de erro em caso de exce��o
                UpdateResponseTextBox($"Erro durante o envio: {ex.Message}");
            }
        }
/*        private async Task ExecuteSendParcelas()
        {
            textBoxResponse.Clear();

            try
            {
                var apiData = idEmpresa_API;

                var responseMessages = new List<string>();

                UpdateResponseTextBox("Aguarde, realizando upload de Parcelas...");
                await SendParcelas2(idEmpresa_API);
                UpdateResponseTextBox("Upload realizado para Parcelas");

                // Mensagem final indicando que todos os uploads foram realizados
                UpdateResponseTextBox("Todos os Uploads Realizados");
            }
            catch (Exception ex)
            {
                // Atualiza a resposta com a mensagem de erro em caso de exce��o
                UpdateResponseTextBox($"Erro durante o envio: {ex.Message}");
            }
        }*/

        private async Task SendClientes2(int idEmpresa_API)
        {
            {
                var clientes = GetClientes2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = clientes };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_cliente", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }

        }

        private async Task SendFormaPagamento2(int idEmpresa_API)
        {
            {
                var formaPagamento = GetFormaPagamento2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = formaPagamento };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_forma_pagamento", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendGrupoPagamento2(int idEmpresa_API)
        {
            {
                var grupoPagamento = GetGrupoPagamento2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = grupoPagamento };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_grupo_pagamento", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendFuncionarios2(int idEmpresa_API)
        {
            {
                var funcionarios = GetFuncionario2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = funcionarios };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_funcionario", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendCampanhaPromocao2(int idEmpresa_API)
        {
            {
                var campanhas = GetCampanhaPromocao2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = campanhas };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_campanha", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendProdutoPromocao2(int idEmpresa_API)
        {
            {
                var produtospromos = GetProdutoPromocao2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = produtospromos };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_produto_promocao", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendPdv2(int idEmpresa_API)
        {
            {
                var pdvs = GetPdv2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = pdvs };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_terminal_pdv", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendProdutos2(int idEmpresa_API)
        {
            {
                var produtos = GetProdutos2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = produtos };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_grupo_produto", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }

        private async Task SendProdutosL2(int idEmpresa_API)
        {
            int batchSize = GetBatchSizeFromParametrosGestor(); // Obt�m o tamanho do lote da tabela

            var produtosL = GetProdutosL2FromDatabase();
            int totalProducts = produtosL.Length;
            int numberOfBatches = (totalProducts + batchSize - 1) / batchSize; // Calcula o n�mero de lotes necess�rio

            UpdateResponseTextBox($"Total de Produtos: {totalProducts}");

            for (int batchNumber = 0; batchNumber < numberOfBatches; batchNumber++)
            {
                int start = batchNumber * batchSize;
                int end = Math.Min(start + batchSize, totalProducts);

                var currentBatch = produtosL.Skip(start).Take(end - start).ToArray();

                UpdateResponseTextBox($"Enviando lote {batchNumber + 1} contendo {currentBatch.Length} produtos...");

                try
                {
                    var dados = new { id_empresa = idEmpresa_API, dados = currentBatch };
                    var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_produto", dados);

                    if (responseContent.Contains("success"))
                    {
                        UpdateResponseTextBox($"Lote {batchNumber + 1} de {numberOfBatches} enviado com sucesso.");
                    }
                    else
                    {
                        UpdateResponseTextBox($"Lote {batchNumber + 1} de {numberOfBatches} teve problemas: {responseContent}");
                    }
                }
                catch (Exception ex)
                {
                    UpdateResponseTextBox($"Erro ao enviar lote {batchNumber + 1}: {ex.Message}");
                }
            }

            UpdateResponseTextBox("Envio de todos os lotes conclu�do.");
        }

        private async Task SendMovimentoCaixa2(int idEmpresa_API)
        {
            {
                var caixas = GetMovimentoCaixa2FromDatabase();
                var dados = new { id_empresa = idEmpresa_API, dados = caixas };
                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=CadastrosRestService&method=upload_movimento_caixa", dados);
                UpdateResponseTextBox(FormatApiResponse(responseContent));
            }
        }
        private async Task SendNotaFiscalCabecalho2(int idEmpresa_API)
        {
            int batchSize = GetBatchSizeFromParametrosGestor();

            var notasFiscais = GetNotaFiscalCabecalho2FromDatabase();
            int totalNotas = notasFiscais.Length;
            int numberOfBatches = (totalNotas + batchSize - 1) / batchSize;

            for (int batchNumber = 0; batchNumber < numberOfBatches; batchNumber++)
            {
                int start = batchNumber * batchSize;
                int end = Math.Min(start + batchSize, totalNotas);
                var batchNotasFiscais = notasFiscais.Skip(start).Take(end - start).ToArray();

                // Monta a lista do lote com os itens e pagamentos de cada nota
                var loteDados = new List<object>();

                foreach (var nota in batchNotasFiscais)
                {
                    var itens = GetNotaFiscalDetalhes2FromDatabase(nota.id);
                    var pagamentos = GetNotaFiscalPagamento2FromDatabase(nota.id);

                    loteDados.Add(new
                    {
                        nota.id,
                        nota.observacao,
                        nota.tipo_nota_fiscal_id,
                        nota.cliente_id,
                        nota.terminal_pdv_id,
                        nota.situacao_movimentacao_id,
                        nota.data_autorizacao,
                        nota.data_venda,
                        nota.nome_cliente,
                        nota.numero,
                        nota.serie,
                        nota.total_produtos,
                        nota.total_servicos,
                        nota.total_acrescimo,
                        nota.total_desconto,
                        nota.total_geral,
                        nota.valor_icms,
                        nota.valor_pis,
                        nota.valor_cofins,
                        nota.valor_ipi,
                        nota.valor_iss,
                        nota.vendedor_id,
                        nota.caixa_id,
                        nota.total_nf,
                        itens,
                        pagamento = pagamentos
                    });
                }

                var dadosParaEnvio = new
                {
                    id_empresa = idEmpresa_API,
                    dados = loteDados.ToArray()
                };

                var responseContent = await PostAsync($"{apiUrl}/rest.php?class=NotaRestService&method=upload_nota_fiscal", dadosParaEnvio);

                // Opcional: Trate o responseContent para log ou controle de erros
            }
        }


        private int GetBatchSizeFromParametrosGestor()
        {
            int batchSize = 50; // Valor padr�o de fallback

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT QTD_REG_ENVIO FROM PARAMETROS_GESTOR";

                    object result = command.ExecuteScalar();

                    if (result != null &&
                        result != DBNull.Value &&
                        int.TryParse(result.ToString(), out int parsedBatchSize) &&
                        parsedBatchSize > 0)
                    {
                        batchSize = parsedBatchSize;
                    }
                }
            }

            return batchSize;
        }

        private dynamic[] GetFuncionario2FromDatabase()
        {
            var funcionarios = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT 
                        c.id, 
                        c.NOME AS descricao,
                        c.SENHA AS senha,
                        CASE 
                            WHEN EXISTS (SELECT 1 FROM CARGO_FUNCIONARIO cf WHERE cf.ID_FUNCIONARIO = c.id AND cf.ID_CARGO = 2) 
                            THEN '1' 
                            ELSE '0' 
                        END AS caixa,
                        CASE 
                            WHEN EXISTS (SELECT 1 FROM CARGO_FUNCIONARIO cf WHERE cf.ID_FUNCIONARIO = c.id AND cf.ID_CARGO = 1) 
                            THEN '1' 
                            ELSE '0' 
                        END AS vendedor,
                        c.login
                    FROM vw_lst_funcionario_empresa c;";

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            funcionarios.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("id")),
                                descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? null : reader.GetString(reader.GetOrdinal("descricao")),
                                vendedor = reader.GetString(reader.GetOrdinal("vendedor")),
                                caixa = reader.GetString(reader.GetOrdinal("caixa")),
                                senha = reader.IsDBNull(reader.GetOrdinal("senha")) ? null : reader.GetString(reader.GetOrdinal("senha")),
                                login = reader.IsDBNull(reader.GetOrdinal("login")) ? null : reader.GetString(reader.GetOrdinal("login"))
                            });
                        }
                    }
                }
            }
            return funcionarios.ToArray();
        }

        private dynamic[] GetProdutos2FromDatabase() //Produto grupo
        {
            var produtos = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
				    SELECT
                        C.id,
                        c.nome as descricao,
                        COALESCE(c.TAXA_COMISSAO,0)
                        as percentual_comissao
                    FROM 
                        GRUPO_PRODUTO C ";
                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            produtos.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("id")),
                                descricao = reader.GetString(reader.GetOrdinal("descricao")),
                                percentual_comissao = reader.GetDecimal(reader.GetOrdinal("percentual_comissao"))
                            });
                        }
                    }
                }
            }
            return produtos.ToArray();
        }

        private dynamic[] GetCampanhaPromocao2FromDatabase() // Produto grupo
        {
            var campanhas = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT
                        ID, DESCRICAO, DATA_INICIAL, DATA_FINAL,
                        STATUS, ID_EMPRESA, ID_TIPO_PRECO_PRODUTO
                    FROM
                        CAMPANHA_PROMOCAO_PRODUTO
                    WHERE 
                        ID_EMPRESA = @idEmpresa_TERMINAL";

                    var pEmpresa = command.CreateParameter();
                    pEmpresa.ParameterName = "@idEmpresa_TERMINAL";
                    pEmpresa.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(pEmpresa);

                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            campanhas.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("ID")),
                                descricao = reader.IsDBNull(reader.GetOrdinal("DESCRICAO"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("DESCRICAO")),

                                data_inicial = reader.IsDBNull(reader.GetOrdinal("DATA_INICIAL"))
                                    ? null
                                    : reader.GetDateTime(reader.GetOrdinal("DATA_INICIAL")).ToString("yyyy-MM-dd HH:mm:ss"),

                                data_final = reader.IsDBNull(reader.GetOrdinal("DATA_FINAL"))
                                    ? null
                                    : reader.GetDateTime(reader.GetOrdinal("DATA_FINAL")).ToString("yyyy-MM-dd HH:mm:ss"),

                                empresa_id = reader.GetInt32(reader.GetOrdinal("ID_EMPRESA")),

                                status = reader.IsDBNull(reader.GetOrdinal("STATUS"))
                                    ? null
                                    : reader.GetString(reader.GetOrdinal("STATUS")),

                                id_tipo_preco_produto = reader.IsDBNull(reader.GetOrdinal("ID_TIPO_PRECO_PRODUTO"))
                                    ? (int?)null
                                    : reader.GetInt32(reader.GetOrdinal("ID_TIPO_PRECO_PRODUTO"))
                            });
                        }
                    }
                }

            }
            return campanhas.ToArray();
        }



        private dynamic[] GetProdutoPromocao2FromDatabase() //Produto grupo
        {
            var produtospromos = new List<dynamic>();

            using (DbConnection connection = CreateConnection())
            {
                connection.Open();

                using (DbCommand command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT PP.ID,
                        COALESCE(PP.QUANTIDADE_EM_PROMOCAO, 0) AS QUANTIDADE_EM_PROMOCAO, 
                        COALESCE(PP.QUANTIDADE_MAXIMA_CLIENTE, 0) AS QUANTIDADE_MAXIMA_CLIENTE,
                        PP.VALOR, 
                        COALESCE(PP.qtdja_vendida, 0) AS qtda_vendida, 
				        COALESCE(pp.ID_TIPO_ESTOQUE_PRODUTO,0) as id_tipo_estoque_produto,
                        pd.VALOR_PRODUTO AS VALOR_VENDA, pd.ID_PRODUTO,C.ID as id_campanha_promo_prod
                    FROM PRODUTO_PROMOCAO PP
                    INNER JOIN CAMPANHA_PROMOCAO_PRODUTO C ON C.ID = PP.ID_CAMPANHA_PROMO_PROD
                    INNER JOIN PRECO_PRODUTO pd ON pd.ID_PRODUTO = PP.ID_PRODUTO 
                        AND pd.ID_ECF_EMPRESA = C.id_empresa 
                        AND pd.ID_TIPO_PRECO_PRODUTO = C.ID_TIPO_PRECO_PRODUTO
                    WHERE 
                        C.id_empresa = @idEmpresa_TERMINAL";

                    var pEmpresa = command.CreateParameter();
                    pEmpresa.ParameterName = "@idEmpresa_TERMINAL";
                    pEmpresa.Value = idEmpresa_TERMINAL;
                    command.Parameters.Add(pEmpresa);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            produtospromos.Add(new
                            {
                                id = reader.GetInt32(reader.GetOrdinal("ID")),
                                produto_id = reader.GetInt32(reader.GetOrdinal("ID_PRODUTO")),
                                quantidade_em_promocao = reader.GetDecimal(reader.GetOrdinal("QUANTIDADE_EM_PROMOCAO")),
                                quantidade_maxima_cliente = reader.GetDecimal(reader.GetOrdinal("QUANTIDADE_MAXIMA_CLIENTE")),
                                valor = reader.GetDecimal(reader.GetOrdinal("VALOR")),
                                id_campanha_promo_prod = reader.GetInt32(reader.GetOrdinal("id_campanha_promo_prod")),
                                qtda_vendida = reader.GetDecimal(reader.GetOrdinal("qtda_vendida")),
                                id_tipo_estoque_produto = reader.GetInt32(reader.GetOrdinal("id_tipo_estoque_produto")),
                                valor_venda = reader.GetDecimal(reader.GetOrdinal("VALOR_VENDA")),
                            });
                        }
                    }
                }
            }
            return produtospromos.ToArray();

        }

        private dynamic[] GetMovimentoCaixa2FromDatabase()
        {
            var caixas = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
                SELECT id, descricao,
                       DATA_SANGRIA AS data_movimento, valor,
                       ID_OPERADOR AS funcionario_id,
                       1 AS tipo_movimento_caixa_id,
                       COALESCE(ID_TERMINAL_PDV, '') AS terminal_pdv_id
                FROM ECF_SANGRIA
                UNION
                SELECT id, descricao,
                       DATA_SUPRIMENTO AS data_movimento, valor,
                       ID_OPERADOR AS funcionario_id,
                       2 AS tipo_movimento_caixa_id,
                       COALESCE(ID_TERMINAL_PDV, '') AS terminal_pdv_id
                FROM ECF_SUPRIMENTO
                ", connection);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        caixas.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            descricao = reader.GetString(reader.GetOrdinal("descricao")),
                            data_movimento = reader.GetDateTime(reader.GetOrdinal("data_movimento")),
                            valor = reader.GetDecimal(reader.GetOrdinal("valor")),
                            funcionario_id = reader.GetInt32(reader.GetOrdinal("funcionario_id")),
                            tipo_movimento_caixa_id = reader.GetInt32(reader.GetOrdinal("tipo_movimento_caixa_id")),
                            terminal_pdv_id = reader.GetInt32(reader.GetOrdinal("terminal_pdv_id")),
                        });
                    }
                }
            }

            return caixas.ToArray();
        }

        private dynamic[] GetProdutosL2FromDatabase() //Produto unidade
        {
            var produtosL = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
            SELECT c.id, LEFT(c.NOME, 50) as descricao, gc.NOME as subgrupo_produto, c.ID_STATUS_PRODUTO,
            cd.DESCRICAO as marca_produto, ud.NOME as unidade,
            mr.FANTASIA as fornecedor, c.referencia, COALESCE(c.gtin, '') as gtin,
            COALESCE(c.VALOR_COMISSAO, 0) as percentual_comissao, COALESCE(c.ESTOQUE_MIN, 0) as quantidade_minima,
            COALESCE(c.ESTOQUE_MAX, 0) as quantidade_maxima, 
            c.id_grupo_produto as grupo_produto_id, COALESCE(c.PRODUTO_DESTAQUE,'N') as produto_destaque,
            c.ncm, c.cest, c.VENDE_STORE, c.DESCRICAO_STORE as descricao_store,
            COALESCE((select pp.VALOR_PRODUTO from preco_PRODUTO pp where pp.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and pp.ID_TIPO_PRECO_PRODUTO=1 and pp.ID_PRODUTO=c.id), 0) as preco1,
            COALESCE((select pp.VALOR_PRODUTO from preco_PRODUTO pp where pp.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and pp.ID_TIPO_PRECO_PRODUTO=2 and pp.ID_PRODUTO=c.id), 0) as preco2,
            COALESCE((select pp.VALOR_PRODUTO from preco_PRODUTO pp where pp.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and pp.ID_TIPO_PRECO_PRODUTO=3 and pp.ID_PRODUTO=c.id), 0) as preco3,
            COALESCE((select pp.VALOR_PRODUTO from preco_PRODUTO pp where pp.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and pp.ID_TIPO_PRECO_PRODUTO=4 and pp.ID_PRODUTO=c.id), 0) as preco4,
            coalesce((select ep.QTD_ESTOQUE from ESTOQUE_PRODUTO ep where ep.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and ep.ID_TIPO_ESTOQUE_PRODUTO=1 and ep.ID_PRODUTO=c.id),0) qtd_estoque,
            coalesce((select ep.QTD_ESTOQUE from ESTOQUE_PRODUTO ep where ep.ID_ECF_EMPRESA=@idEmpresa_TERMINAL and ep.ID_TIPO_ESTOQUE_PRODUTO=2 and ep.ID_PRODUTO=c.id),0) qtd_estoque2,
            COALESCE(c.TAXA_COMISSAO,0) as percentual_comissao, ud.PODE_FRACIONAR as pode_fracionar
            FROM produto C
            INNER JOIN produto_marca cd ON cd.ID = c.ID_PRODUTO_MARCA
            INNER JOIN SUBGRUPO_PRODUTO gc ON gc.ID = c.ID_SUBGRUPO_PRODUTO
            INNER JOIN UNIDADE_PRODUTO ud ON ud.ID = c.ID_UNIDADE_PRODUTO
            INNER JOIN FORNECEDOR mr ON mr.ID = c.ID_FORNECEDOR
            ", connection);

                command.Parameters.AddWithValue("@idEmpresa_TERMINAL", idEmpresa_TERMINAL);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        produtosL.Add(new
                        {
                            grupo_produto_id = reader.GetInt32(reader.GetOrdinal("grupo_produto_id")),
                            id_status_produto = reader.GetInt32(reader.GetOrdinal("ID_STATUS_PRODUTO")),
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? null : reader.GetString(reader.GetOrdinal("descricao")),
                            subgrupo_produto = reader.IsDBNull(reader.GetOrdinal("subgrupo_produto")) ? null : reader.GetString(reader.GetOrdinal("subgrupo_produto")),
                            marca_produto = reader.IsDBNull(reader.GetOrdinal("marca_produto")) ? null : reader.GetString(reader.GetOrdinal("marca_produto")),
                            unidade = reader.IsDBNull(reader.GetOrdinal("unidade")) ? null : reader.GetString(reader.GetOrdinal("unidade")),
                            fornecedor = reader.IsDBNull(reader.GetOrdinal("fornecedor")) ? null : reader.GetString(reader.GetOrdinal("fornecedor")),
                            referencia = reader.IsDBNull(reader.GetOrdinal("referencia")) ? null : reader.GetString(reader.GetOrdinal("referencia")),
                            vende_store = reader.IsDBNull(reader.GetOrdinal("VENDE_STORE")) ? null : reader.GetString(reader.GetOrdinal("VENDE_STORE")),
                            gtin = reader.IsDBNull(reader.GetOrdinal("gtin")) ? null : reader.GetString(reader.GetOrdinal("gtin")),
                            percentual_comissao = reader.GetDecimal(reader.GetOrdinal("percentual_comissao")),
                            quantidade_minima = reader.GetDecimal(reader.GetOrdinal("quantidade_minima")),
                            quantidade_maxima = reader.GetDecimal(reader.GetOrdinal("quantidade_maxima")),
                            cest = reader.IsDBNull(reader.GetOrdinal("cest")) ? null : reader.GetString(reader.GetOrdinal("cest")),
                            pode_fracionar = reader.IsDBNull(reader.GetOrdinal("pode_fracionar")) ? null : reader.GetString(reader.GetOrdinal("pode_fracionar")),
                            ncm = reader.IsDBNull(reader.GetOrdinal("ncm")) ? null : reader.GetString(reader.GetOrdinal("ncm")),
                            produto_destaque = reader.IsDBNull(reader.GetOrdinal("produto_destaque")) ? null : reader.GetString(reader.GetOrdinal("produto_destaque")),
                            descricao_store = reader.IsDBNull(reader.GetOrdinal("descricao_store")) ? null : reader.GetString(reader.GetOrdinal("descricao_store")),
                            preco1 = reader.GetDecimal(reader.GetOrdinal("preco1")),
                            preco2 = reader.GetDecimal(reader.GetOrdinal("preco2")),
                            qtd_estoque = reader.GetDecimal(reader.GetOrdinal("qtd_estoque")),
                        });
                    }
                }
            }

            return produtosL.ToArray();
        }

        private dynamic[] GetClientes2FromDatabase()
        {
            var clientes = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
                    SELECT c.id, c.nome AS razao_social, c.cpf_cnpj AS cnpj, c.fantasia AS nome_fantasia,
                    gc.NOME AS grupo_cliente, cd.NOMECIDADE AS cidade, c.bairro, c.complemento, c.data_nascimento
                    FROM cliente c
                    INNER JOIN cidade cd ON cd.IDCIDADE = c.ID_CIDADE
                    INNER JOIN GRUPO_CLIENTE gc ON gc.id = c.ID_GRUPO_CLIENTE
                    ", connection);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        clientes.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            razao_social = reader.IsDBNull(reader.GetOrdinal("razao_social")) ? null : reader.GetString(reader.GetOrdinal("razao_social")),
                            cnpj = reader.IsDBNull(reader.GetOrdinal("cnpj")) ? null : reader.GetString(reader.GetOrdinal("cnpj")),
                            nome_fantasia = reader.IsDBNull(reader.GetOrdinal("nome_fantasia")) ? null : reader.GetString(reader.GetOrdinal("nome_fantasia")),
                            grupo_cliente = reader.IsDBNull(reader.GetOrdinal("grupo_cliente")) ? null : reader.GetString(reader.GetOrdinal("grupo_cliente")),
                            complemento = reader.IsDBNull(reader.GetOrdinal("complemento")) ? null : reader.GetString(reader.GetOrdinal("complemento")),
                            data_nascimento = reader.IsDBNull(reader.GetOrdinal("data_nascimento"))
        ? null
        : reader.GetDateTime(reader.GetOrdinal("data_nascimento")).ToString("yyyy-MM-dd"),
                            /*vende_store = reader.IsDBNull(reader.GetOrdinal("vende_store")) ? null : reader.GetString(reader.GetOrdinal("vende_store")),*/
                            cidade = reader.IsDBNull(reader.GetOrdinal("cidade")) ? null : reader.GetString(reader.GetOrdinal("cidade")),
                            bairro = reader.IsDBNull(reader.GetOrdinal("bairro")) ? null : reader.GetString(reader.GetOrdinal("bairro"))
                        });
                    }
                }
            }

            return clientes.ToArray();
        }

        private dynamic[] GetGrupoPagamento2FromDatabase()
        {
            var grupoPagamento = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
                select ID, DESCRICAO from GRUPO_PAGAMENTO
                ", connection);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        grupoPagamento.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("ID")),
                            descricao = reader.IsDBNull(reader.GetOrdinal("DESCRICAO")) ? null : reader.GetString(reader.GetOrdinal("DESCRICAO"))
                        });
                    }
                }
            }

            return grupoPagamento.ToArray();
        }

        private dynamic[] GetFormaPagamento2FromDatabase()
        {
            var formaPagamento = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
                select c.id, c.DESCRICAO as descricao, c.ID_GRUPO_PAGAMENTO as grupo_pagamento_id,
                COALESCE(c.TAXA_COMISSAO,0) as percentual_comissao
                from ECF_TIPO_PAGAMENTO c", connection);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        formaPagamento.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? null : reader.GetString(reader.GetOrdinal("descricao")),
                            percentual_comissao = reader.GetDecimal(reader.GetOrdinal("percentual_comissao")),
                            grupo_pagamento_id = reader.GetInt32(reader.GetOrdinal("grupo_pagamento_id"))
                        });
                    }
                }
            }

            return formaPagamento.ToArray();
        }

        private dynamic[] GetPdv2FromDatabase()
        {
            var pdvs = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
                select c.id, c.DESCRICAO as descricao
                from TERMINAL_PDV c", connection);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        pdvs.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            descricao = reader.IsDBNull(reader.GetOrdinal("descricao")) ? null : reader.GetString(reader.GetOrdinal("descricao")),
                        });
                    }
                }
            }

            return pdvs.ToArray();
        }

        private dynamic[] GetNotaFiscalCabecalho2FromDatabase()
        {
            var notasFiscais = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
                  SELECT 
                    c.id,
                    COALESCE(c.OBS_ADICIONAL, '') AS observacao,
                    c.ID_TIPO_NOTA_FISCAL AS tipo_nota_fiscal_id,
                    c.ID_CLIENTE AS cliente_id,
                    COALESCE(c.ID_TERMINAL_PDV, 1) AS terminal_pdv_id,
                    COALESCE(c.ID_SITUACAO, 0) AS situacao_movimentacao_id,
                    COALESCE(c.DATA_AUTORIZACAONF, c.DATA_EMISSAO) AS data_autorizacao,
                    c.DATA_EMISSAO AS data_venda,
                    c.NOME_CLIENTE,
                    COALESCE(c.NUMERO, 0) AS numero,
                    c.SERIE,
                    c.TOTAL_PRODUTOS,
                    c.TOTAL_NF AS total_nf,
                    COALESCE(c.TOTALSERVICOS, 0) AS total_servicos,
                    COALESCE(c.ACRESCIMO, 0) AS total_acrescimo,
                    c.DESCONTO AS total_desconto,
                    c.total_produtos AS total_geral,
                    c.ICMS AS valor_icms,
                    c.PIS AS valor_pis,
                    c.COFINS AS valor_cofins,
                    c.IPI AS valor_ipi,
                    COALESCE(c.ISSQN, 0) AS valor_iss,
                    c.ID_VENDEDOR AS vendedor_id,
                    c.ID_ECF_FUNCIONARIO AS caixa_id,
                    COALESCE(c.VALOR_RECEBIDO, 0) AS valor_recebido
                FROM 
                    NOTA_FISCAL_CABECALHO c 
                WHERE 
                    c.ID_TIPO_NOTA_FISCAL IN (1, 2, 3, 4, 5)
                    AND c.ID_SITUACAO IN (8, 9)
                ", connection);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        notasFiscais.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            observacao = reader.GetString(reader.GetOrdinal("observacao")),
                            tipo_nota_fiscal_id = reader.GetInt32(reader.GetOrdinal("tipo_nota_fiscal_id")),
                            cliente_id = reader.GetInt32(reader.GetOrdinal("cliente_id")),
                            terminal_pdv_id = reader.GetInt32(reader.GetOrdinal("terminal_pdv_id")),
                            situacao_movimentacao_id = reader.GetInt32(reader.GetOrdinal("situacao_movimentacao_id")),
                            data_autorizacao = reader.GetDateTime(reader.GetOrdinal("data_autorizacao")),
                            data_venda = reader.GetDateTime(reader.GetOrdinal("data_venda")),
                            nome_cliente = reader.GetString(reader.GetOrdinal("nome_cliente")),
                            numero = reader.GetInt32(reader.GetOrdinal("numero")),
                            serie = reader.GetString(reader.GetOrdinal("serie")),
                            total_produtos = reader.GetDecimal(reader.GetOrdinal("total_produtos")),
                            total_servicos = reader.GetDecimal(reader.GetOrdinal("total_servicos")),
                            total_acrescimo = reader.GetDecimal(reader.GetOrdinal("total_acrescimo")),
                            total_nf = reader.GetDecimal(reader.GetOrdinal("total_nf")),
                            total_desconto = reader.GetDecimal(reader.GetOrdinal("total_desconto")),
                            total_geral = reader.GetDecimal(reader.GetOrdinal("total_geral")),
                            valor_icms = reader.GetDecimal(reader.GetOrdinal("valor_icms")),
                            valor_pis = reader.GetDecimal(reader.GetOrdinal("valor_pis")),
                            valor_cofins = reader.GetDecimal(reader.GetOrdinal("valor_cofins")),
                            valor_ipi = reader.GetDecimal(reader.GetOrdinal("valor_ipi")),
                            valor_iss = reader.GetDecimal(reader.GetOrdinal("valor_iss")),
                            vendedor_id = reader.GetInt32(reader.GetOrdinal("vendedor_id")),
                            caixa_id = reader.GetInt32(reader.GetOrdinal("caixa_id"))
                        });
                    }
                }
            }
            return notasFiscais.ToArray();
        }

        private dynamic[] GetNotaFiscalDetalhes2FromDatabase(int notaFiscalId)
        {
            var itensDetalhes = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
            SELECT 
              d.id,
                COALESCE(d.ID_PRODUTO,1) AS produto_id,
                COALESCE(d.VALOR_UNITARIO,0) as valor_unitario,
                COALESCE(d.CUSTO_PROD, 0) AS valor_custo,
                COALESCE(d.quantidade,0) as quantidade,
                COALESCE(d.VALOR_PRODUTOS,0) AS sub_total,
                COALESCE(d.desconto, 0) AS desconto,
                COALESCE(d.acrescimo,0) as acrescimo,
                COALESCE(d.VALOR_TOTAL,0) as valor_total,
                COALESCE(d.icms, 0) AS valor_pis,
	            COALESCE(d.pis, 0) AS valor_cofins,
	            COALESCE(d.cofins, 0) AS valor_ipi,
	            COALESCE(d.issqn, 0) AS valor_icms,
	            COALESCE(d.ipi, 0) AS valor_iss,
                COALESCE(d.cst, 00) AS id_cst_icms,
                COALESCE(d.CST_PIS, 99) AS id_cst_pis,
                COALESCE(d.CST_COFINS, 1) AS id_cst_cofins,
                COALESCE(d.CST_IPI, 00) AS id_cst_ipi,
	            d.NOME_PRODUTO
            FROM NOTA_FISCAL_DETALHE d 
            WHERE d.ID_NF_CABECALHO = @notaFiscalId
            ", connection);

                command.Parameters.AddWithValue("@notaFiscalId", notaFiscalId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        itensDetalhes.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            produto_id = reader.GetInt32(reader.GetOrdinal("produto_id")),
                            valor_unitario = reader.GetDecimal(reader.GetOrdinal("valor_unitario")),
                            nome_produto = reader.GetString(reader.GetOrdinal("nome_produto")),
                            valor_custo = reader.GetDecimal(reader.GetOrdinal("valor_custo")),
                            quantidade = reader.GetDecimal(reader.GetOrdinal("quantidade")),
                            sub_total = reader.GetDecimal(reader.GetOrdinal("sub_total")),
                            desconto = reader.GetDecimal(reader.GetOrdinal("desconto")),
                            acrescimo = reader.GetDecimal(reader.GetOrdinal("acrescimo")),
                            valor_total = reader.GetDecimal(reader.GetOrdinal("valor_total")),
                            valor_icms = reader.GetDecimal(reader.GetOrdinal("valor_icms")),
                            valor_pis = reader.GetDecimal(reader.GetOrdinal("valor_pis")),
                            valor_cofins = reader.GetDecimal(reader.GetOrdinal("valor_cofins")),
                            valor_ipi = reader.GetDecimal(reader.GetOrdinal("valor_ipi")),
                            valor_iss = reader.GetDecimal(reader.GetOrdinal("valor_iss")),
                            id_cst_icms = reader.GetInt32(reader.GetOrdinal("id_cst_icms")),
                            id_cst_pis = reader.GetInt32(reader.GetOrdinal("id_cst_pis")),
                            id_cst_cofins = reader.GetInt32(reader.GetOrdinal("id_cst_cofins")),
                            id_cst_ipi = reader.GetInt32(reader.GetOrdinal("id_cst_ipi"))
                        });
                    }
                }
            }
            return itensDetalhes.ToArray();
        }

        private dynamic[] GetNotaFiscalPagamento2FromDatabase(int notaFiscalId)
        {
            var pagamentos = new List<dynamic>();

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var command = new SqlCommand(@"
            SELECT 
                p.id,
                COALESCE(p.ID_ECF_TIPO_PAGAMENTO, 0) AS forma_pagamento_id,
                COALESCE(p.valor, 0) AS valor
            FROM NOTA_FISCAL_TIPO_PAGAMENTO p
            WHERE p.ID_NF_CABECALHO = @notaFiscalId
            ", connection);

                command.Parameters.AddWithValue("@notaFiscalId", notaFiscalId);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        pagamentos.Add(new
                        {
                            id = reader.GetInt32(reader.GetOrdinal("id")),
                            forma_pagamento_id = reader.GetInt32(reader.GetOrdinal("forma_pagamento_id")),
                            valor = reader.GetDecimal(reader.GetOrdinal("valor"))
                        });
                    }
                }
            }
            return pagamentos.ToArray();
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            notifyIcon1.Visible = false;
            CheckInitialSendStatus();
        }
        private void SetButtonStatesForInitialLoad()
        {
            button1EnvioInicial.Enabled = true; // Ativa o bot�o de envio inicial
            buttonStartLoop.Enabled = false;
            buttonCancelLoop.Enabled = false;
            buttonSendAll.Enabled = false;

            button1EnvioInicial.BackColor = SystemColors.Control;
            buttonStartLoop.BackColor = SystemColors.ControlLight;
            buttonCancelLoop.BackColor = SystemColors.ControlLight;
            buttonSendAll.BackColor = SystemColors.ControlLight;
        }

        private void UpdateButtonStatesAfterInitialSend()
        {
            button1EnvioInicial.Enabled = false; // Desativa o bot�o de envio inicial ap�s o primeiro envio
            buttonStartLoop.Enabled = true;
            buttonCancelLoop.Enabled = true;
            buttonSendAll.Enabled = true;

            button1EnvioInicial.BackColor = SystemColors.ControlLight;
            buttonStartLoop.BackColor = SystemColors.Control;
            buttonCancelLoop.BackColor = SystemColors.Control;
            buttonSendAll.BackColor = SystemColors.Control;
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                notifyIcon1.Visible = false;
            }
        }
        private void LogErrorToFile(Exception ex, string context = null)
        {
            try
            {
                string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string logFilePath = Path.Combine(logDirectory, "Errors.txt");
                var sb = new StringBuilder();
                sb.AppendLine($"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {context ?? "Error"} ---");
                sb.AppendLine(ex.GetType().FullName + ": " + ex.Message);
                if (ex.InnerException != null)
                {
                    sb.AppendLine("InnerException: " + ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message);
                    sb.AppendLine(ex.InnerException.StackTrace ?? "");
                }
                sb.AppendLine(ex.StackTrace ?? "");
                sb.AppendLine();

                File.AppendAllText(logFilePath, sb.ToString());
            }
            catch
            {
                // Não propagar erros do logger
            }
        }
        private void sairToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void textBoxResponse_TextChanged(object sender, EventArgs e)
        {

        }
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10) // Ajuste o tempo conforme necess�rio
        };

        private void groupBoxActions_Enter(object sender, EventArgs e)
        {

        }
    }
}