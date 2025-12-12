namespace LsiGestor
{
    internal class ItemNotaFiscal
    {
        public int Id { get; set; }
        public int ProdutoId { get; set; }
        public decimal ValorCusto { get; set; }
        public decimal Quantidade { get; set; }
        public decimal SubTotal { get; set; }
        public decimal Desconto { get; set; }
        public decimal ValorTotal { get; set; }
        public decimal ValorIcms { get; set; }
        public int IdCstIcms { get; set; }
        public int IdCstPis { get; set; }
        public int IdCstCofins { get; set; }
        public int IdCstIpi { get; set; }
    }
}