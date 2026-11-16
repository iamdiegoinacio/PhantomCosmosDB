Pesquisa avançada — Azure Cosmos DB: consumo de RUs, preços internacionais e custo real de queries
Referência: 16 de novembro de 2026 | Microsoft Azure Cosmos DB | Valores em USD, sem impostos e descontos contratuais

O Azure Cosmos DB possui uma característica importante: o custo de executar uma consulta não depende simplesmente da quantidade de consultas realizadas, mas dos recursos que cada operação consome e do modelo de cobrança escolhido.

A pesquisa nas fontes técnicas da Microsoft e em tabelas regionais extraídas da Azure Retail Prices API revela diferenças consideráveis entre países.
EUA — East US 2
US$ 0,008
por 100 RU/s por hora, provisionado


Brasil — Brazil South
US$ 0,012
por 100 RU/s por hora, provisionado



Na comparação, a região Brazil South apresenta um preço 50% maior para throughput provisionado do que East US 2. No modelo serverless, a diferença também é de 50%: US$ 0,375 contra US$ 0,25 por milhão de RUs. Esses valores aparecem em tabelas regionais de uma fonte secundária que informa atualização em 5 de novembro de 2026 com dados da API de preços da Azure. 
Usage AI+2

1. Como as RUs realmente funcionam
Uma Request Unit (RU) é uma unidade normalizada de consumo computacional, utilizada pelo Cosmos DB para representar processamento, memória e operações de entrada e saída necessárias para uma operação.

É fundamental distinguir:
- RU: quantidade de recursos consumidos por uma operação.
- RU/s: capacidade de processamento disponível por segundo.
- Throughput provisionado: você paga pela capacidade contratada, mesmo sem utilizá-la integralmente.
- Serverless: você paga pelas RUs efetivamente consumidas, sem contratar uma capacidade fixa de RU/s.
Por exemplo, uma leitura direta de um documento de aproximadamente 1 KB, informando seu id e sua chave de partição, consome normalmente 1 RU. Uma consulta SQL pode consumir 3, 10, 100 ou milhares de RUs, dependendo da execução.

A Microsoft confirma que tamanho dos documentos, quantidade de resultados, indexação, particionamento e complexidade das consultas afetam diretamente o consumo. 
Microsoft Learn+1

2. Comparação de preços em 14 países
Para tornar os valores comparáveis, utilizei:
- 730 horas por mês.
- Cobrança pay-as-you-go, sem descontos.
- Uma região de gravação, sem zonas de disponibilidade adicionais.
- API NoSQL no modelo baseado em RUs, não Cosmos DB for MongoDB vCore.
- Armazenamento e rede separados do preço de throughput.
O preço é determinado principalmente pela região Azure onde os recursos estão implantados, não pela nacionalidade do proprietário da assinatura.
Tabela internacional de preços
País / Região Azure	1.000 RU/s por mês	1 milhão de RUs serverless	Armazenamento por GB/mês
🇺🇸 EUA — East US 2	US$ 58,40	US$ 0,250	US$ 0,250
🇳🇱 Holanda — West Europe	US$ 58,40	US$ 0,305	US$ 0,250
🇬🇧 Reino Unido — UK South	US$ 58,40	US$ 0,297	US$ 0,250
🇸🇬 Singapura — Southeast Asia	US$ 58,40	US$ 0,285	US$ 0,250
🇰🇷 Coreia do Sul — Korea Central	US$ 58,40	US$ 0,271	US$ 0,250
🇨🇦 Canadá — Canada Central	US$ 64,24	US$ 0,275	US$ 0,275
🇲🇽 México — Mexico Central	US$ 64,24	US$ 0,275	US$ 0,275
🇯🇵 Japão — Japan East	US$ 65,70	US$ 0,285	US$ 0,2825
🇮🇳 Índia — Central India	US$ 66,43	US$ 0,285	US$ 0,285
🇦🇺 Austrália — Australia East	US$ 67,16	US$ 0,285	US$ 0,2875
🇫🇷 França — France Central	US$ 73,00	US$ 0,297	US$ 0,3125
🇦🇪 Emirados Árabes — UAE North	US$ 77,09	US$ 0,311	US$ 0,330
🇧🇷 Brasil — Brazil South	US$ 87,60	US$ 0,375	US$ 0,375
🇿🇦 África do Sul — South Africa North	US$ 85,41	US$ 0,335	US$ 0,365

Valores derivados das tabelas regionais do Usage.ai, que informa utilizar a Azure Retail Prices API, com atualização em 05/11/2026. Não são cotações contratuais emitidas diretamente pela Microsoft. 
Usage AI+12
Custo de 1.000 RU/s provisionadas por mês
USD, 730 horas, sem armazenamento ou descontos


US$ 0US$ 25US$ 50US$ 75US$ 100EUAHolandaReino UnidoCanadáJapãoÍndiaAustráliaFrançaBrasil














Uma particularidade: Brazil Southeast aparece nas tabelas consultadas com US$ 113,88/mês para 1.000 RU/s, mais caro que Brazil South, embora sua tarifa serverless listada seja de US$ 0,25/milhão de RUs. Como isso produz uma diferença incomum entre modalidades, eu confirmaria a disponibilidade e o SKU diretamente no Azure antes de escolher essa região. 
Usage AI


Interpretação: hospedar no Brasil não é necessariamente a alternativa mais econômica. Entretanto, escolher East US 2 apenas pelo preço pode ser um erro quando a aplicação precisa de baixa latência no Brasil, requisitos de residência dos dados ou replica informações internacionalmente.

3. Quantas RUs cada operação consome?
A documentação da Microsoft fornece medições ilustrativas para operações comuns. Isso permite começar o dimensionamento sem precisar inventar uma equivalência fixa para todas as consultas.
Operação com documento pequeno	RUs estimadas
Leitura direta por ID + partition key, 1 KB	1
Inserção simples, aproximadamente 1 KB	5
Atualização, aproximadamente 1 KB	10
Exclusão simples, aproximadamente 1 KB	5
Query indexada retornando até 100 itens	10


Microsoft Learn
O efeito do tamanho dos documentos
Valores ilustrativos publicados pela Microsoft para operações de leitura e gravação:
Documento	Leitura (RU)	Gravação (RU)
1 KB	1,00	4,95
4 KB	1,14	6,67
16 KB	1,67	9,52
64 KB	4,76	22,67
128 KB	9,95	48,19
256 KB	20,28	98,29
1.024 KB (1 MB)	145,90	625,00


Microsoft Learn
Esses valores são exemplos, não tarifas universais: o número de propriedades indexadas, a operação e a configuração podem alterar significativamente o consumo.

Um documento de 1 MB pode consumir muito mais RUs que um documento de 1 KB. Por outro lado, uma consulta que recupera somente algumas propriedades, usando bons índices, pode economizar recursos.
4. Custo de uma query SQL de exemplo
Considere um sistema com documentos de pedidos e chave de partição /tenantId:
SELECT TOP 100
    c.id,
    c.total,
    c.status
FROM c
WHERE c.tenantId = @tenantId
  AND c.status = "paid"
ORDER BY c.createdAt DESC


Suponha que a consulta esteja adequadamente indexada, o índice composto necessário esteja configurado e o processamento completo consuma 10 RUs por execução.

Esse é um cenário de simulação baseado na referência de consulta eficiente da Microsoft, não uma medição real dessa SQL.
Custo por volume — serverless
Consultas	RUs consumidas	Brasil	EUA
1	10	US$ 0,00000375	US$ 0,00000250
1.000	10 mil	US$ 0,00375	US$ 0,00250
100 mil	1 milhão	US$ 0,375	US$ 0,25
1 milhão	10 milhões	US$ 3,75	US$ 2,50
10 milhões	100 milhões	US$ 37,50	US$ 25,00
50 milhões	500 milhões	US$ 187,50	US$ 125,00
100 milhões	1 bilhão	US$ 375,00	US$ 250,00

Somente processamento de queries. Armazenamento, eventuais encargos de rede e demais operações do banco são cobrados separadamente.

O resultado é interessante: um milhão de consultas indexadas consumindo 10 RUs cada custaria apenas US$ 3,75 de processamento serverless em Brazil South.

Contudo, a situação muda completamente se a mesma consulta passar de 10 para 1.000 RUs, em razão de varreduras, projeções extensas ou falta de filtros adequados.
Exemplo de uma consulta ineficiente
Uma query que recupera milhares de registros pode consumir cerca de 1.000 RUs ou mais. A Microsoft utiliza como exemplo uma consulta que retorna mil documentos de 1 KB com consumo de aproximadamente 1.000 RUs. 
Microsoft Learn


Para um milhão de execuções:
- Com 10 RU/query: US$ 3,75 no Brasil.
- Com 100 RU/query: US$ 37,50.
- Com 1.000 RU/query: US$ 375,00.
Ou seja, otimizar uma consulta pode gerar uma redução de custo de 90% ou mais, mesmo mantendo exatamente o mesmo plano e região.
5. Simulador de custo de queries
Altere os campos para estimar seu próprio volume mensal no modelo serverless e comparar com o throughput provisionado.
Região AzureBrasil — Brazil South

RU por query


Queries por mês


Armazenamento total (GB)




Custo mensal estimado — serverless
US$ 7,50
Processamento

US$ 3,75

Armazenamento

US$ 3,75



Provisionado — referência teórica
US$ 38,79

Capacidade: 400 RU/s, dimensionada apenas pela média de 3,81 RU/s; sem considerar picos.

O cálculo não inclui free tier, descontos, backup, transferência de dados nem outras operações. A capacidade real deve ser dimensionada também para os picos de utilização e limites de partição.

Restaurar valores do exemplo

6. Provisionado, autoscale ou serverless?
Esses três modelos podem produzir custos muito diferentes para a mesma quantidade de operações.
Comparação prática — Brazil South
Considere 10 GB de armazenamento, uma região e ausência de free tier.
Modelo	Configuração	Custo mensal estimado
Provisionado manual	400 RU/s fixos	US$ 38,79
Provisionado manual	1.000 RU/s fixos	US$ 91,35
Autoscale	Máximo de 1.000 RU/s, sempre no mínimo de 100 RU/s	US$ 16,89
Autoscale	Máximo de 1.000 RU/s, escalando até o máximo em todas as horas	US$ 135,15
Serverless	10 milhões de RUs/mês	US$ 7,50
Serverless	100 milhões de RUs/mês	US$ 41,25
Serverless	500 milhões de RUs/mês	US$ 191,25

A Microsoft determina que o autoscale seja cobrado pelo maior throughput alcançado dentro de cada hora. Sua tarifa em uma região de gravação equivale a 1,5 vez a do provisionamento manual, e a configuração começa com Tmax = 1.000 RU/s. 
Microsoft Learn+1

Quando o provisionado passa a compensar?
Em Brazil South, para 400 RU/s provisionadas:
\[
\text{Custo mensal}=4\times0{,}012\times730
=\text{US\$ }35{,}04
\]

No serverless, o mesmo valor compra:
\[
\frac{35{,}04}{0{,}375}\times1.000.000
=\boxed{93{,}44\text{ milhões de RUs}}
\]

Logo, o ponto de equilíbrio matemático fica próximo de 93,44 milhões de RUs mensais antes de considerar free tier, custos adicionais e requisitos de desempenho.

Esse volume equivale a cerca de 9,34 milhões de consultas de 10 RUs.

Na prática, a decisão depende também de distribuição temporal: 400 RU/s não podem atender instantaneamente a um pico de 1.000 RU/s, mesmo quando o consumo médio mensal é baixo.
7. Custos escondidos que precisam entrar no orçamento
Componente	Como afeta o preço
Armazenamento	Cobrado por GB consumido; inclui dados e índices transacionais
Índices	Ocupam espaço e aumentam o custo de determinadas escritas
Múltiplas regiões	Throughput e armazenamento cobrados por região
Multi-region writes	Preço maior de throughput para habilitar gravações em várias regiões
Zonas de disponibilidade	Podem acrescentar 25% à cobrança de throughput ou RUs serverless, conforme configuração
Backup contínuo	Armazenamento adicional nos planos de retenção mais longos
Tráfego de rede	Saída de dados e replicação entre regiões podem gerar cobranças
Gateway dedicado/cache integrado	Recursos adicionais que exigem dimensionamento e cobrança próprios


Microsoft Azure+3
Para exemplificar a replicação, um contêiner de 400 RU/s e 10 GB em Brazil South e East US 2, com apenas uma região gravável, teria uma base estimada de US$ 64,65/mês para throughput e armazenamento nas duas regiões, antes dos custos de transferência.

No backup, a documentação utiliza US$ 0,20/GB/mês como exemplo para retenção contínua de 30/35 dias na região West US. A retenção de 7 dias não cobra pelo armazenamento do backup, mas operações de restauração continuam sujeitas a encargos. 
Microsoft Learn

8. Como reduzir efetivamente o consumo de RUs
Os fatores que mais merecem atenção são:
1. Point reads: usar o id e a chave de partição com a operação de leitura direta, em vez de executar SQL quando o documento é conhecido.
2. Partition key: selecionar uma chave alinhada aos filtros mais frequentes para evitar consultas entre diversas partições.
3. Índices compostos: úteis em consultas com ORDER BY de múltiplas propriedades e certos filtros combinados.
4. Projeções: devolver apenas campos necessários, evitando SELECT * quando há documentos grandes.
5. Evitar scans: operações como UPPER() sobre um campo no filtro podem impedir o uso eficiente do índice; CONTAINS também pode gerar varreduras de índice relativamente caras.
6. Paginação: controlar o volume retornado e utilizar continuation tokens em vez de grandes deslocamentos com OFFSET.
7. Consistência: leituras com consistência Strong ou Bounded Staleness podem consumir aproximadamente o dobro das RUs.
8. Cache: quando adequado, o cache integrado pode servir leituras sem consumir RUs do backend, mas o gateway dedicado também tem seu custo.

Microsoft Learn+3
Uma observação particularmente relevante: a Microsoft documenta um overhead de aproximadamente 2–3 RUs por partição física em determinadas consultas entre partições. Em bases grandes, esse custo de distribuição pode tornar uma consulta aparentemente simples significativamente mais cara. 
Microsoft Learn

9. Como descobrir o consumo real de uma query
O número decisivo que você deve medir no ambiente é o cabeçalho:

x-ms-request-charge

Ele informa as RUs gastas pela requisição. Em consultas paginadas, você precisa somar as RUs de todas as páginas para obter o custo de toda a consulta. 
Microsoft Learn


Por exemplo, usando o SDK de Python:
from azure.cosmos import CosmosClientimport osclient = CosmosClient(    os.environ["COSMOS_ENDPOINT"],    credential=os.environ["COSMOS_KEY"])container = client.get_database_client("MinhaBase") \                  .get_container_client("Pedidos")query = """SELECT TOP 100 c.id, c.totalFROM cWHERE c.tenantId = @tenantId  AND c.status = @status"""results = container.query_items(    query=query,    parameters=[        {"name": "@tenantId", "value": "empresa-123"},        {"name": "@status", "value": "paid"}    ],    partition_key="empresa-123",    populate_query_metrics=True)items = list(results)# Cabeçalho da última resposta recebidaru_ultima_resposta = float(    container.client_connection.last_response_headers[        "x-ms-request-charge"    ]



Esse exemplo lê a cobrança da última resposta, não necessariamente a soma de todas as páginas. Para um levantamento de produção, registre as RUs por página/continuação ou utilize telemetria agregada do SDK e Azure Monitor.

Eu recomendaria medir cada SQL importante com pelo menos três situações: retorno vazio, retorno típico e retorno máximo esperado, variando também o tamanho e a distribuição da base. Assim é possível dimensionar custos e picos de RU/s de maneira confiável.
10. Free tier e possibilidade de custo zero
Uma das descobertas mais importantes é que o Cosmos DB possui uma camada gratuita vitalícia, desde que habilitada na criação da conta elegível:
1.000 RU/s
Throughput gratuito elegível


25 GB
Armazenamento gratuito elegível



A camada é aplicável às contas elegíveis com throughput provisionado e autoscale, mas não às contas serverless. Só é permitido um benefício de conta Cosmos DB free tier por assinatura Azure, com os limites gratuitos aplicados no nível da conta, não por contêiner ou região. 
Microsoft Learn


Isso muda bastante a análise: uma aplicação pequena pode processar milhões de requisições mensais sem pagar pela capacidade básica, desde que respeite os limites e não habilite componentes pagos adicionais.
11. Conclusão técnica
Os resultados mais importantes desta pesquisa são:
- O custo de uma query de 10 RUs pode ser de apenas frações de centavo no serverless, mas seu consumo real precisa ser medido.
- O throughput provisionado pode oferecer uma economia enorme em operações frequentes, especialmente com tráfego constante.
- A diferença regional pode atingir 50% ou mais, mas latência, residência dos dados e transferência entre regiões podem superar essa economia.
- Os índices e a escolha da chave de partição frequentemente têm mais impacto no custo final do que mudar de país.
- A camada gratuita de 1.000 RU/s e 25 GB pode tornar o provisionamento a melhor opção para aplicações pequenas.
Para os valores regionais definitivos de uma implantação, use o preçário oficial do Azure Cosmos DB, a documentação da Azure Retail Prices API e o Cosmos DB Capacity Calculator.

O próximo passo para estimar o custo de uma query específica é conhecer seu SQL, a quantidade aproximada de documentos, o tamanho médio de cada documento e a partition key. Com esses dados, podemos estimar o intervalo de RUs, throughput necessário para os picos e custo mensal de execução no Brasil e no exterior — distinguindo a simulação do consumo efetivamente medido.