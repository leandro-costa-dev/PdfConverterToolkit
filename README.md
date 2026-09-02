# PdfConverterToolkit

Conversor de PDF para Windows, **local, offline e em lote**, com três conversões no mesmo
lugar — e as três disponíveis tanto na **janela** quanto na **linha de comando**:

| Conversão | O que faz |
| --- | --- |
| **PDF → Word** | Gera `.docx` em quatro modos, do layout fiel ao texto puro |
| **PDF → Imagem** | Uma imagem por página em **JPG, PNG, BMP, GIF ou TIFF**, por pixels ou por DPI |
| **Compactar PDF** | Reduz o tamanho rasterizando as páginas e remontando um PDF novo |

Nada precisa ser instalado além do .NET: todas as bibliotecas vêm por NuGet
([PdfPig](https://github.com/UglyToad/PdfPig) lê o PDF,
[Open XML SDK](https://github.com/dotnet/Open-XML-SDK) escreve o `.docx`,
[PDFtoImage](https://github.com/sungaila/PDFtoImage) desenha as páginas com o PDFium e
[PDFsharp](https://www.pdfsharp.net/) remonta o PDF compactado).

## Os quatro modos de PDF → Word

| Modo | O que sai | Quando usar |
| --- | --- | --- |
| **Layout fiel** | Texto editável com a formatação original, tabelas de verdade, imagens no lugar, uma seção por página | O padrão: é o que os conversores on-line fazem, mas aqui local e em lote |
| **Texto** | Só o texto corrido, editável e pesquisável | Quando o layout não importa e o arquivo precisa ficar pequeno |
| **Página como imagem** | Cada página vira uma figura dentro do Word | Fidelidade visual total, inclusive assinaturas — sem texto editável |
| **Imagem + texto** | Por página: a figura fiel e, abaixo, o texto editável daquela página | Quando você precisa das duas coisas |

Nos três últimos — os modos rápidos — vários PDFs podem sair **num único `.docx`**, na ordem
em que foram informados: **Arquivo único** na aba, `--arquivo-unico` na linha de comando.

O modo fiel preserva:

| Elemento | Como é reproduzido |
| --- | --- |
| Texto | Parágrafos editáveis com fonte, corpo, negrito, itálico e cor originais |
| Layout | Alinhamento (esquerda/centro/direita/justificado), recuos e entrelinha medidos do PDF |
| Página | Tamanho, orientação (retrato/paisagem) e margens de cada página, uma seção por página |
| Tabelas | `<w:tbl>` reais: colunas proporcionais, bordas por célula, células mescladas e cor de fundo |
| Tabelas sem grade | Detectadas pelo alinhamento das colunas e geradas sem borda |
| Imagens | Extraídas e inseridas no lugar; as que o leitor não decodifica são recortadas da página renderizada |
| Títulos | Marcados como Título 1..3 — alimentam o painel de navegação e o sumário automático |
| Hiperlinks | Continuam clicáveis |
| Páginas digitalizadas | Sem camada de texto, entram como imagem da página inteira (em vez de sair em branco) |

O `.docx` gerado é validado contra o esquema do Office Open XML e abre no Word,
LibreOffice Writer e Google Docs.

## Estrutura do repositório

```
src/
  PdfConverterToolkit.Core/      o que as três conversões compartilham (net10.0)
                                 rasterização (PDFium), expansão de entradas, nomes de
                                 saída sem sobrescrever, progresso e relatório de lote
  PdfConverterToolkit.Docx/      PDF → Word (net10.0)
    Model/                       geometria e modelo de layout (parágrafos, tabelas, imagens)
    Analysis/                    leitura do PDF: linhas, tabelas, imagens, parágrafos
    Writer/                      escrita do .docx (Open XML), fiel e nos modos rápidos
  PdfConverterToolkit.Imaging/   PDF → imagem e compactação (net10.0-windows)
  PdfConverterToolkit.Cli/       pdfconv.exe — linha de comando e lote
  PdfConverterToolkit.App/       PdfConverterToolkit.exe — janela Windows Forms, três abas
```

`Core` e `Docx` são multiplataforma. `Imaging` depende do GDI+ para gravar **BMP, GIF e
TIFF** (formatos que o Skia não codifica), e por isso ele — e quem o usa, a CLI e o
aplicativo — só roda no Windows. JPEG e PNG saem direto do Skia, sem passar pelo GDI+.

## Pré-requisitos

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (ou Visual Studio 2022+)

## Compilar

```bash
dotnet build PdfConverterToolkit.slnx -c Release
```

Os executáveis ficam em
`src\PdfConverterToolkit.App\bin\Release\net10.0-windows\PdfConverterToolkit.exe` e
`src\PdfConverterToolkit.Cli\bin\Release\net10.0-windows\pdfconv.exe`.

## Aplicativo (janela)

```bash
dotnet run --project src/PdfConverterToolkit.App
```

As três abas funcionam do mesmo jeito:

1. **Adicionar PDFs…** ou **arraste** arquivos/pastas para a janela. Marque **Incluir
   subpastas** antes de soltar uma pasta para varrer o que há dentro dela.
2. Ajuste as opções da aba.
3. Escolha a **pasta de saída** — vazia, cada arquivo é gravado ao lado do PDF de origem
   (na aba de imagens, tudo vai para a pasta do primeiro PDF).
4. Clique no botão de ação. O andamento aparece página a página, **Cancelar** interrompe
   entre páginas e o painel **Resultado** mostra uma linha por arquivo ao final.

Arquivos existentes nunca são sobrescritos: recebem sufixo `(1)`, `(2)`…
Um erro em um PDF não interrompe o lote — ele é registrado e a fila continua.

### Aba PDF → Word

O modo escolhido decide quais opções valem: as caixas de reconstrução (tabelas, imagens,
títulos, links, cabeçalhos) são do **layout fiel**; **DPI** e **qualidade** valem onde a
página é rasterizada (nos modos de imagem e, no modo fiel, nas páginas sem texto).

**Arquivo único** junta todos os PDFs da fila num só `.docx`, na ordem em que aparecem na
lista — útil para montar um dossiê a partir de vários anexos digitalizados. Vale nos três
modos rápidos (**Texto**, **Página como imagem** e **Imagem + texto**) e fica desabilitada no
**layout fiel**, que sempre grava um `.docx` por PDF. O campo **Nome do arquivo** é preenchido
sozinho com `<nome do primeiro PDF>_unificado.docx` e pode ser trocado; um nome com caracteres
proibidos é corrigido em vez de recusado. Como o resultado é um arquivo só, ele não pode ficar
"ao lado de cada PDF": sem pasta de saída escolhida, ele vai para a pasta do primeiro PDF.

No arquivo único cada PDF começa em página nova e cada página mantém a orientação (retrato ou
paisagem) que tinha no original. Um PDF ilegível — ou, no modo Texto, um sem camada de texto —
é registrado no **Resultado** e apenas pulado; os demais continuam entrando no documento.

### Aba PDF → Imagem

Deixe **Largura** e **Altura** em 0 para dimensionar pelo **DPI**. Com **Manter proporção**
marcado, informe apenas uma das dimensões. **Incluir anotações e assinaturas** (marcado por
padrão) renderiza anotações e campos de formulário — assinaturas digitais, carimbos gov.br,
tinta — que o PDFium ignora por padrão. **Qualidade** só se aplica ao JPEG.

Cada página vira um arquivo: `nomeDoPdf_p001.jpg`, `nomeDoPdf_p002.jpg`, …

### Aba Compactar PDF

Gera `nomeDoPdf_compactado.pdf`. Menor DPI e menor qualidade = arquivo menor.

> **Importante:** a compactação **rasteriza** as páginas (o texto vira imagem, perdendo
> seleção e busca). É ideal para **PDFs digitalizados ou com muitas imagens**; PDFs somente
> de texto tendem a **aumentar** de tamanho — por isso existe **Pular se não reduzir o
> tamanho**, marcado por padrão.

## Linha de comando

```bash
pdfconv <comando> <arquivo.pdf | pasta | curinga> [mais entradas...] [opções]
```

Os comandos são `word`, `imagem` e `compactar`. Sem comando explícito, `word` é assumido —
`pdfconv edital.pdf` converte para Word.

Opções válidas em todos os comandos:

| Opção | Efeito |
| --- | --- |
| `-d, --pasta <pasta>` | Pasta de saída (padrão: a mesma do PDF) |
| `-r, --recursivo` | Nas pastas informadas, incluir as subpastas |
| `--senha <senha>` | Senha do PDF protegido |
| `--dpi <n>` | Resolução das páginas rasterizadas |
| `--qualidade <1-100>` | Qualidade JPEG |
| `--sobrescrever` | Regravar por cima de um arquivo existente |
| `-h, --help` | Ajuda |

### `pdfconv word`

| Opção | Efeito |
| --- | --- |
| `-o, --saida <arquivo.docx>` | Nome do arquivo de saída (apenas com um PDF de entrada, ou com `--arquivo-unico`) |
| `--modo <modo>` | `fiel` (padrão), `texto`, `imagem` ou `imagem-texto` |
| `--arquivo-unico` | Juntar todos os PDFs num único `.docx`, na ordem informada (não vale no modo `fiel`) |
| `--sem-tabelas` | Não reconstruir tabelas |
| `--sem-tabelas-sem-borda` | Reconstruir apenas tabelas com linhas visíveis |
| `--sem-imagens` | Não extrair imagens |
| `--sem-titulos` | Não marcar títulos (Título 1..3) |
| `--sem-links` | Não preservar hiperlinks |
| `--sem-raster` | Deixar em branco as páginas sem camada de texto |
| `--limpar-cabecalhos` | Remover cabeçalhos/rodapés repetidos em quase todas as páginas |
| `--linhas-fixas` | Manter as quebras de linha do PDF em vez de texto corrido |

### `pdfconv imagem`

| Opção | Efeito |
| --- | --- |
| `--formato <fmt>` | `jpg` (padrão), `png`, `bmp`, `gif` ou `tif` |
| `--largura <px>` / `--altura <px>` | Tamanho em pixels (0 = dimensionar pelo DPI) |
| `--sem-proporcao` | Não preservar a proporção ao redimensionar |
| `--sem-anotacoes` | Não renderizar anotações/assinaturas |

### `pdfconv compactar`

| Opção | Efeito |
| --- | --- |
| `--manter-maiores` | Gravar mesmo quando o resultado não reduzir o tamanho |

### Exemplos

```bash
pdfconv edital.pdf
pdfconv word "C:\Editais\*.pdf" -d "C:\Editais\Word" --limpar-cabecalhos
pdfconv word contrato.pdf --modo imagem-texto --dpi 200
pdfconv word "C:\Anexos" --modo imagem --arquivo-unico -o "C:\Anexos\dossie.docx"
pdfconv imagem contrato.pdf --formato png --largura 1600
pdfconv compactar "C:\Digitalizados" -r --dpi 100 --qualidade 50
```

A entrada aceita arquivos, pastas e curingas (`*.pdf`). Códigos de saída: `0` tudo certo,
`1` sem argumentos, `2` erro de uso, `3` nenhum PDF encontrado, `4` algum arquivo falhou.

## Usar como biblioteca

```csharp
using PdfConverterToolkit.Core;
using PdfConverterToolkit.Docx;
using PdfConverterToolkit.Imaging;

// PDF -> Word, um arquivo
var opcoes = new WordOptions { Mode = WordMode.Faithful, Dpi = 150 };
var resultado = WordConverter.Convert("edital.pdf", "edital.docx", opcoes);
Console.WriteLine($"{resultado.PageCount} páginas, {resultado.Details?.TableCount} tabelas");

// PDF -> Word, lote (um .docx por PDF)
var relatorio = new BatchReport();
WordConverter.ConvertBatch(["a.pdf", "b.pdf"], @"C:\Saida", opcoes, relatorio);

// PDF -> Word, vários PDFs num único .docx (modos texto, imagem e imagem-texto)
var unico = new WordOptions { Mode = WordMode.Image, Dpi = 150 };
WordConverter.ConvertMerged(["a.pdf", "b.pdf"], @"C:\Saida\dossie.docx", unico, relatorio);

// PDF -> imagens
PdfImageExporter.ExportBatch(
    ["a.pdf"], @"C:\Saida",
    new ImageExportOptions { Format = ImageOutputFormat.Png, Width = 1600 },
    relatorio);

// Compactar
PdfCompressor.Compress("digitalizado.pdf", "menor.pdf", new CompressionOptions { Dpi = 100 });
```

Todas as operações aceitam um `IProgress<>` para acompanhar o andamento e um
`CancellationToken` para cancelar entre páginas. `PdfDocxConverter.Convert` continua
disponível para quem quiser falar direto com o motor de layout fiel.

## Como funciona o modo fiel

1. **Leitura** — o PdfPig devolve cada letra com posição, fonte, corpo e cor; os caminhos
   vetoriais da página; e as imagens embutidas. Tudo é convertido para coordenadas de tela
   (origem no canto superior esquerdo), já aplicando a rotação declarada na página.
2. **Tabelas** — os traços horizontais e verticais que se cruzam formam grades conexas.
   As divisórias viram bordas de linha e coluna; a ausência de divisória entre duas células
   vizinhas vira mesclagem (`gridSpan` / `vMerge`); retângulos preenchidos viram cor de fundo.
   Uma tabela cortada pela quebra de página é completada pelos traços verticais.
3. **Texto** — as letras são agrupadas em linhas pela linha de base e em trechos de mesmo
   estilo. Os espaços são medidos entre **avanços de fonte**, não entre desenhos — é isso que
   evita o clássico "1 1" no lugar de "11".
4. **Parágrafos** — linhas consecutivas se juntam quando a anterior chega à margem direita,
   o corpo de fonte não muda e não há marcador de lista. Daí saem alinhamento, recuos,
   entrelinha e nível de título.
5. **Escrita** — uma seção por página, com o tamanho e as margens do original.

## Limitações conhecidas

- **PDF digitalizado sem OCR** não tem texto para extrair: no modo fiel as páginas entram
  como imagem e o modo Texto avisa que não há o que extrair. Passe um OCR antes se precisar
  de texto editável.
- No modo fiel, o documento gerado pode ficar com **mais páginas** que o original (36 contra
  30, num edital de 30 páginas). O Word refaz as quebras de linha com métricas de fonte um
  pouco diferentes das do PDF; use `--linhas-fixas` para reduzir a diferença, ao custo do
  texto não refluir sozinho ao editar.
- Colunas jornalísticas (duas colunas de texto corrido) são lidas de cima para baixo, não
  coluna a coluna.
- Fórmulas, gráficos vetoriais e formulários não são reconstruídos como objetos do Word.
- O `.docx` não suporta camada de texto invisível sobre a imagem (como num "PDF
  pesquisável"). Por isso o modo **Imagem + texto** empilha, por página, a figura fiel e o
  texto editável — em vez de sobrepor os dois.
- GIF é paletizado em 256 cores automaticamente ao salvar.

---

Este projeto unifica os antigos **PdfToDocxConverter** (motor de layout fiel) e
**PdfToImageConverter** (imagens, compactação e os modos rápidos de Word), que
compartilhavam rasterização, geração de `.docx` e toda a mecânica de lote.
