
namespace LsiGestor
{
    internal class NotaFiscal
    {
        internal string? numero;

        public int Id { get; set; }
        public string Observacao { get; set; }
        public int TipoNotaFiscalId { get; set; }
        public int ClienteId { get; set; }
        public int TerminalPdvId { get; set; }
        public int SituacaoMovimentacaoId { get; set; }
        public DateTime DataAutorizacao { get; set; }
        public string NomeCliente { get; set; }
        public int Numero { get; set; }
        public int Serie { get; set; }
        public decimal TotalProdutos { get; set; }
        public decimal TotalServicos { get; set; }
        public int VendedorId { get; set; }
        public DateTime? Data_autorizacao { get; internal set; }
    }
}