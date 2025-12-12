public class DetalheParcela
{
    public int id { get; set; }
    public int id_contas_parcelas { get; set; }
    public DateTime? data_vencimento { get; set; }
    public DateTime? data_pagamento { get; set; }
    public int? numero_parcela { get; set; }
    public decimal valor { get; set; }
    public decimal taxa_juros { get; set; }
    public decimal taxa_multa { get; set; }
    public decimal taxa_desconto { get; set; }
    public decimal valor_juros { get; set; }
    public int id_situacao_parcela { get; set; }
    public decimal valor_pago { get; set; }
    public int id_empresa_pago { get; set; }
    public string boleto_impresso { get; set; }
    public string stbaixa_retorno { get; set; }
    public int nosso_numero { get; set; }
    public string status_estorno { get; set; }
    public string status_previsao { get; set; }
}

public class PagamentoParcela
{
    public int id { get; set; }
    public int id_contas_detalhes { get; set; }
    public int id_ecf_tipo_pagamento { get; set; }
    public decimal valor_pagamento { get; set; }
    public DateTime? data_estorno { get; set; }
    public string status_estorno { get; set; }
    public int? id_original { get; set; }
    public int id_terminal_pdv { get; set; }
    public string tipo_baixa { get; set; }
    public int? id_cpag_terminal { get; set; }
    public int id_conta_bancaria { get; set; }
}
