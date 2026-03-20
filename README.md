# LSI Gestor

Aplicação desktop desenvolvida em C# (Windows Forms) para gerenciamento de notas fiscais, contas a receber e pagamentos, com conexão a banco de dados Firebird e execução de rotinas internas para controle financeiro.

O sistema permite manipular dados de faturamento, pagamentos e contas através de classes de modelo e conexão direta com banco.

---

## 📌 Estrutura do projeto

Arquivos do projeto:

```
Properties/
App.config
ContaReceber.cs
FbConnection.cs
Form1.cs
Form1.Designer.cs
Form1.resx
GestorForm.sln
ItemNotaFiscal.cs
LSI Gestor.csproj
NotaFiscal.cs
PagamentoNotaFiscal.cs
Program.cs
images.ico
```

---

## 📌 Descrição dos arquivos

### Program.cs

Ponto de entrada da aplicação.

Responsável por iniciar o Windows Forms.

```csharp
Application.Run(new Form1());
```

Função:

* Inicializar aplicação
* Abrir tela principal

---

### Form1.cs

Tela principal do sistema.

Responsável por:

* Interface
* Execução de rotinas
* Controle das operações
* Chamadas ao banco

Aqui fica a lógica principal do sistema.

---

### Form1.Designer.cs

Arquivo gerado automaticamente.

Contém:

* Botões
* Labels
* TextBox
* Grid
* Layout da tela

Não deve ser alterado manualmente.

---

### Form1.resx

Arquivo de recursos da interface.

Contém:

* Ícones
* Imagens
* Textos
* Configurações visuais

---

### App.config

Arquivo de configuração.

Usado para:

* String de conexão
* Configurações do sistema
* Parâmetros de execução

Exemplo comum:

```
connectionString
provider
database
user
password
```

---

### FbConnection.cs

Classe responsável pela conexão com Firebird.

Funções:

* Abrir conexão
* Fechar conexão
* Executar comandos SQL
* Consultar dados

Usado por todo o sistema.

Banco utilizado:

Firebird

---

### NotaFiscal.cs

Classe modelo de nota fiscal.

Representa dados como:

* Número
* Cliente
* Valor
* Data
* Status

Usado para manipular registros.

---

### ItemNotaFiscal.cs

Classe de itens da nota fiscal.

Contém:

* Produto
* Quantidade
* Valor
* Total

Relacionada com NotaFiscal.

---

### PagamentoNotaFiscal.cs

Classe de pagamento.

Representa:

* Valor pago
* Data
* Forma de pagamento

Relacionada com NotaFiscal.

---

### ContaReceber.cs

Classe de contas a receber.

Controla:

* Valores pendentes
* Parcelas
* Vencimento
* Pagamentos

Usado no controle financeiro.

---

### LSI Gestor.csproj

Arquivo do projeto.

Define:

* Framework
* Dependências
* Tipo de aplicação
* Build

---

### GestorForm.sln

Arquivo da solução do Visual Studio.

Usado para abrir o projeto completo.

---

### Properties/

Configurações do projeto:

* Assembly
* Recursos
* Configuração de build

---

### images.ico

Ícone do sistema.

Usado na aplicação.

---

## 📌 Funcionamento geral

Fluxo do sistema:

```
Program.cs
   ↓
Form1
   ↓
Conecta banco
   ↓
Carrega dados
   ↓
Manipula notas
   ↓
Manipula pagamentos
   ↓
Atualiza tela
```

---

## 📌 Banco de dados

O sistema utiliza Firebird.

A conexão é feita em:

```
FbConnection.cs
```

E configurada em:

```
App.config
```

---

## 📌 Funcionalidades

* Controle de notas fiscais
* Controle de pagamentos
* Controle de contas a receber
* Consulta de dados
* Conexão direta com banco
* Interface desktop

---

## 📌 Requisitos

* Windows
* .NET
* Firebird Client instalado
* Banco configurado
* Visual Studio (para desenvolvimento)

---

## 📌 Como executar

1. Abrir:

```
GestorForm.sln
```

2. Compilar

3. Executar

ou rodar o exe gerado.

---

## 📌 Uso recomendado

* Controle financeiro interno
* Integração com ERP
* Consulta de notas
* Automação de faturamento

---

## 📌 Autor

Gabriel Rebouças
LSI Sistemas

---
