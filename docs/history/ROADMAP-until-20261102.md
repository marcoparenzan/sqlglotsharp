# Stato reale e guida di ripresa — SqlGlotSharp

## Ultima ripresa: API pubblica/L3 e TimeBucket esistenti verificati

Trovati già presenti SqlGlot.Parse/ParseOne/Transpile in __init__.cs, L3Replay e nodo canonical TimeBucket con generator identity Timescale e mapping PG DATE_BIN bounded. Non riscritti. Coordinator ha eseguito build/test: corretta serializzazione messaggio test Result con JsonElement Undefined, resa deterministica la diagnostica required_args nel SOLO plugin Python custom TimeBucket (ordine width,this; upstream intatto), collegato PostgreSQL TryCast al cast_sql come no_trycast_sql upstream. Mapping TimeBucket PG continua a rifiutare TryCast come prova del timestamp, anche se identity Timescale può renderlo secondo PG.

Build finale exit 0, 0 warning/errori; 892 test passati, 0 falliti/saltati, ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-api-central. API/L3/TimeBucket source nuovi precedevano questa ripresa e ora sono verificati. CLI già supporta --read/--write; help/README vanno riallineati al mapping TimeBucket bounded. DATE_BIN solo con width costante positiva fissa, timestamp esplicitamente tipizzato e origin non ambiguo compatibile; offset/timezone/mesi e tipi non provati non vengono approssimati. Nessuna prova eseguita su server DB.

Ripresa: estendere replay Timescale/L3 helper pipelines e SERIAL/IDENTITY DDL, poi nuova misura corpus e documentazione risultato. Canonical corpus esterno immutato e vecchio mismatch pretty legacy non patchato. Demo/nontrivial grammar restano incomplete; non dichiarare porting finale.

## Ripresa precedente: ALTER TABLE PostgreSQL/Timescale

Integrati parser.alter.cs e generator.alter.cs/Postgres ALTER SET, da agenti limitati ma completati/verificati centralmente. Bounded ADD/DROP COLUMN, TYPE/USING/default/nullability, RENAME e SET reloptions. Generatore DROP COLUMN accetta soltanto flag noti inattivi false/null del parser Python; valori attivi non portati ancora rifiutati. Coordinator ha aggiunto 64 confronti pipeline (16 query × PG/Timescale × pretty). RESET nel baseline è Command; parsing C# resta explicit NotPorted, generazione da Command è limitata a RESET identifier-only. Non inventata AST di RESET.

Corretto race nel test Python RESET: script consuma stdin prima di uscire, evitando pipe chiusa; nessun cambio logica/expected. Build finale exit 0, 0 warning/errori; 880 test passati, 0 falliti/saltati, ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-alter-central. Canonical corpus esterno immutato. Vecchia aspettativa dispatch ALTER aggiornata. Prossimo gruppo: API pubblica bounded e L3 replay/transpiling. Cross-dialect Timescale mappings e ulteriori callbacks restano mancanti; demo ancora anteprima.

## Ripresa precedente: argomenti nominati e DDL Timescale/PostgreSQL

Integrato named-args agent: parser.arguments.cs/generator.arguments.cs e routing lambda/assignment bounded per =>/:=, con 8 test live PostgreSQL/Timescale AST/SQL/reparse/cursor. Integrato e completato centralmente l'agente DDL non verificato: parser.properties.cs/generator.properties.cs, CREATE VIEW/MATERIALIZED VIEW e key=value namespace WITH properties. Corrette compilazione object/pretty, collegamento postCreate/postSchema (non omissione di clausole) e parsing WITH nella posizione pre-schema. 44 confronti pipeline DDL fra PG e Timescale, compact/pretty. Bare flags timescaledb.continuous e tsdb.hypertable senza =TRUE sono ParseError anche nel baseline Python: non dichiarati supportati.

Verifica finale centrale: build soluzione exit 0, 0 warning/errori; 845 test passati, 0 falliti/saltati, ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-timescale-ddl-central. Vecchie aspettative stub aggiornate ai nuovi percorsi, preservando confini di lambda/props/classi non portate. Factory test helper ora inizializza dialetto nativamente e può usare l'oracle Timescale per test.

Demo --dialect timescale: create_hypertable('measurements',by_range('ts'),if_not_exists=>TRUE) e CREATE MATERIALIZED VIEW hourly WITH(timescaledb.continuous=true) AS SELECT time_bucket INTERVAL/AVG ... GROUP BY eseguite pretty exit 0. Nessuna esecuzione DB. Python oracle plugin solo sviluppo; nessun backend Python in produzione. Corpus canonico esterno intatto; scoreboard non rieseguito dopo questi incrementi, ultima misura 15.997 passati/1 mismatch storico pretty/10.501 non portati.

Ripresa: completare altre proprietà/ALTER/arg modifier/type literals e formalizzare transpiling Timescale (time_bucket equivalenze non ancora definite, identity-only). L3 replay e corpus recorder6 completo ancora da fare. Priorità PG/Timescale; demo ancora anteprima, non consegna completa. Documentazione riallineata a supporto Timescale attuale.

## Ripresa precedente: prima anteprima TimescaleDB e ON CONFLICT

Timescale identity-only: nuovi dialects/timescale.cs, parsers/timescale.cs, generators/timescale.cs, host registry overlay e factory native (nessun registry generato alterato). Key timescale, ereditarietà Postgres. FIRST/LAST rimossi da copia delle FUNCTIONS affinché due argomenti restino Anonymous; time_bucket/gapfill/locf/interpolate/hypertable/policies conservati come funzioni di estensione, NON mappati a DateBin. Nuovo Python oracle plugin solo test in src/tools/oracle_plugins/timescale.py; Python upstream intatto e nessun backend Python in produzione.

Portato parser.conflict.cs con INSERT ON CONFLICT DO NOTHING/DO UPDATE, keys/predicate/constraint/WHERE nei percorsi bounded. Native generator factory inizializza dialect al ctor, non dopo; aggiunti ctor host PG/Timescale e ByteString/RawString rendering per le regressioni PostgreSQL E-string/dollar-quote, escaping e latin-1 bounded. Vecchie aspettative stub aggiornate. Demo --dialect timescale disponibile.

Verifica finale: build soluzione exit 0, 0 warning/errori; 840 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-timescale-central. Demo timescale TIME_BUCKET INTERVAL/FIRST(value,ts)/LAST(value,ts) e demo upsert ON CONFLICT ... RETURNING entrambe exit 0. Timescale resta identity-only: named args, specific DDL, qualified functions e transpilation equivalenze non completi. Corpus storico immutato, scoreboard non aggiornato da ultima misura (15.997/1/10.501); recorder6 fresh contexts già testati.

Ripresa: argomenti nominati e opzioni DDL/viste Timescale, mantenendo oracle e confini espliciti; ulteriori parti PostgreSQL ancora mancanti. Priorità PostgreSQL/TimescaleDB; demo NON consegna finale.

## Ripresa precedente: confronto AST strutturale e contesto pretty esplicito

Due agenti hanno investigato le 15 differenze. AstComparison.cs confronta l'intero grafo serde, indipendente dall'ordine degli argomenti-dizionario e dagli indici di traversal: classi, valori, ordine LISTE, commenti, posizioni e tipo restano rigorosi; riferimenti parent invalidi sono rifiutati. L1 usa questa comparazione soltanto per risultati AST; errori/warnings sono ancora exact. Tutti i 14 casi originali verificati come grafi completi equivalenti, più 350 dump Python randomized e 140 varianti decorate e tests mutazioni che devono fallire. Nessun cambiamento al parser per imitare un ordine casuale di set Python e nessun expected modificato.

Recorder aggiornato a VERSION6: runtime_context {"sqlglot.pretty": bool} nei boundaries sql/generate/transpile. L2 rispetta fallback globale per pretty omesso/None, override esplicito true/false e rifiuta contesti sconosciuti. Root cause legacy b61bc... confermata: upstream test_pretty_config_override patches global sqlglot.pretty=True, record storico options={} senza contesto. Due test upstream selezionati e fresh regression corpus prodotti sotto D:\Dev\BuildArtifacts\sqlglot-pretty-agent\pretty-context-selected, non nella canonical directory. Legacy case rimane mismatch: richiede registrazione completa aggiornata, NON patch dell'expected.

Verifica finale centrale: build exit 0, 0 warning/errori; 818 test passati, 0 falliti/saltati, ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-corpus-comparison-central. Scoreboard completo su corpus storico v30.27.0-logger-context-full: 26.499 casi, 15.997 passati, 1 differenza L2 base (global pretty), 10.501 non portati. Report D:\Dev\BuildArtifacts\sqlglot-csharp\reports\comparison-central\scoreboard.md e failures.json. Il gate completo NON è superato; molti casi restano mancanti. Canonical corpus integro.

Ripresa: continuare funzionalità SQL PG/TimescaleDB e L3/transpiling; programmare una registrazione completa con recorder6 senza sovrascrivere il corpus di riferimento, per chiudere il mismatch storico. Non aggiungere bypass o inferire contesto dal testo expected. TimescaleDB ancora da implementare; ON CONFLICT/ALTER e type literals incomplete. Priorità PostgreSQL → TimescaleDB; demo ancora anteprima.

## Ripresa precedente: replay L2 reale, DROP/TRUNCATE e misura completa

Integrati L2Replay.cs/CorpusVerifier e parser.drop.cs/generator.drop.cs da due agenti. L2 usa serde e generatore nativi per sql/generate, opzioni/identità/logger/errori rigorosi; transpile boundary testato separatamente ma routing L3 ancora NotPorted. Adapter reflection inizializza generator PG con opzioni base preservando dispatch derivato (manca ctor completo). Fixture JSONBDeleteAtPath e IgnoreNulls con generator base classificati e testati esplicitamente NotPorted, non spacciati per passati. Programmi/file temporanei dell'agente rimossi. DROP TABLE/VIEW e TRUNCATE ordinari verificati contro Python.

Verifica finale: build soluzione exit 0, 0 warning/errori; 763 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-replay-central. Scoreboard centrale su D:\clones\corpus\v30.27.0-logger-context-full: 26.499 casi, 15.983 passati, 15 differenze, 10.501 non portati. Report D:\Dev\BuildArtifacts\sqlglot-csharp\reports\l2-central\scoreboard.md e failures.json. Successo comando significa report generato, NON gate completo superato.

Diagnosi: inizialmente 18 mismatch. Corretti tre cast PostgreSQL del tipo quotato "char" preservando Identifier come kind USERDEFINED con metadati; aggiunti confronti pipeline e rerun completo. Restano 14 mismatch L1 (base10/Postgres4) la cui prima differenza è l'ordine dei modificatori di set-operation nel dump; upstream SET_OP_MODIFIERS è un set Python, C# dato generato è ordinato diversamente. Non sono stati normalizzati/nascosti: verificare equivalenza strutturale e policy deterministica prima di correggere. Un mismatch L2 base differisce per pretty (SELECT multilinea vs monolinea), possibile contesto globale non catturato da recorder, da confermare. Corpus intatto.

L0 mantiene 100% nei quattro dialetti. L2 PG ha 519 passati/0 differenze/810 non portati (1.329 casi). L1 PG dopo fix char: 510 passati/4 differenze/667 non portati (1.181 casi). Queste percentuali NON sono completamento prodotto, e includono differenze tra ambito prioritario e corpus generale. Ripresa: risolvere le 15 differenze con regressioni, poi L3 replay/transpiling e TimescaleDB. ON CONFLICT/ALTER/type literals e altre dipendenze incomplete. Priorità PostgreSQL → TimescaleDB. Demo resta anteprima.

## Ripresa precedente: constraints comuni di tabella/colonna

Integrati parser.constraints.cs e generator.constraints.cs da agenti paralleli: PRIMARY KEY/UNIQUE/CHECK/REFERENCES/FOREIGN KEY e named CONSTRAINT nei percorsi bounded base/PostgreSQL. Shared schema/column routing aggiornato; INCLUDE/storage/tablespace rendering bounded aggiunto per dipendenze di constraint. Modificatori non portati esplicitamente rifiutati. Coordinator ha sostituito vecchie aspettative stub con positivi (incluse compatibilità shared schema INSERT) e aggiunto 30 confronti end-to-end PostgreSQL AST/SQL/error.

Build centrale finale exit 0, 0 warning/errori; 736 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-constraints-central. Demo CREATE measurements con PK composto (device_id,ts), CHECK e FOREIGN KEY REFERENCES eseguita pretty exit 0. Corpus esterno D:\clones\corpus immutato. Scoreboard completo ancora non aggiornato, questi numeri sono test libreria/tooling, non percentuali del corpus completo.

Ripresa: aggiornare misura PostgreSQL L1/L2, completare DROP/ALTER e ON CONFLICT, type literal constructors e TimescaleDB. Remaining constraint modifiers e serial rewrite incompleti. Segnalazione parent link SEMI ancora da indagare. Priorità PostgreSQL → TimescaleDB. Demo non porting completo; nessuna pubblicazione e nessuna esecuzione SQL sul database.

## Ripresa precedente: CREATE TABLE ordinario e colonne tipizzate

Integrati parser.create.cs e generator.create.cs/Postgres overrides da agenti paralleli. CREATE TABLE/IF NOT EXISTS/CTAS, nomi qualificati, tipi scalari, DEFAULT/NOT NULL/NULL nei percorsi testati. Shared _parse_column_def ora supporta colonne tipizzate anche in schema/parameter/CTE; constraints non supportati restano explicit NotPorted. Coordinator ha ripristinato Directory.Build.props originale: percorso artifacts dell'agente non mantenuto. Generatore accetta indexes vuoto generato dal parser, ma indexes attivi restano NotPorted (nessuna opzione omessa).

Verifica finale: build soluzione exit 0, 0 warning/errori; 728 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-create-central. 40 nuovi confronti end-to-end AST/SQL/error oltre ai gruppi agenti. Demo CREATE TABLE IF NOT EXISTS measurements(device_id BIGINT NOT NULL,ts TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,value DECIMAL DEFAULT 0,payload JSONB) eseguita pretty exit 0. Corpus esterno D:\clones\corpus immutato, scoreboard non aggiornato.

Restano PRIMARY KEY/UNIQUE/REFERENCES e altri constraints, serial rewrites, proprietà DDL/altri object kinds, DROP/ALTER e conflitti INSERT, type literal constructors e TimescaleDB. Segnalazione parent-link SEMI rewrite aperta. Ripresa: completare constraints comuni e misurare PostgreSQL corpus L1/L2, poi estensioni TimescaleDB. Priorità invariata PostgreSQL → TimescaleDB. Demo ancora anteprima, nessuna pubblicazione.

## Ripresa precedente: UPDATE/DELETE e RETURNING verificati

Integrati parser.mutations.cs e generator.mutations.cs da due agenti completati, route UPDATE/DELETE e argument order PyDict upstream. Supportati nei percorsi testati UPDATE SET/WHERE/FROM/RETURNING, DELETE FROM/USING/WHERE/RETURNING e CTE. Coordinator ha aggiunto 30 confronti pipeline PostgreSQL AST/SQL/error con CASE, JSON, tuple assignment, intervalli e commenti; vecchie aspettative stub nell'entrypoint aggiornate.

Build finale exit 0, 0 warning/errori; 699 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-mutations-central. Corpus esterno immutato e parallelizzazione operativa. Restano multitable DELETE/cluster e rewrite FROM→JOIN per altri dialetti, conflitti INSERT, DDL/type literals. Prossimo gruppo: CREATE TABLE ordinario parser/generator. TimescaleDB non ancora implementato. Demo è anteprima, non consegna finale.

## Ripresa precedente: INSERT ordinari e RETURNING verificati

Integrati parser.insert.cs e generator.insert.cs da due agenti completati. Statement INSERT route, colonne target/schema, VALUES/SELECT, DEFAULT VALUES e RETURNING nei percorsi ordinari ora supportati. ON CONFLICT rendering presente ma parsing conflitti resta NotPorted; altre varianti speciali esplicitamente bloccate. Coordinator ha corretto ordine keyword nel dizionario Insert dopo mismatch serde rilevato dal nuovo confronto pipeline: costruttore PyDict esplicito conserva l'ordine Python, expected non modificato.

Verifica finale: build soluzione exit 0, 0 warning/errori; 674 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-insert-central. Aggiunti 48 confronti pipeline AST/SQL/error oltre ai gruppi agenti. Demo INSERT measurements(device_id,value) VALUES(1,10),(2,20) RETURNING ... eseguita pretty exit 0; nessuna connessione database e nessuna modifica dati reali. Corpus esterno D:\clones\corpus immutato, scoreboard non aggiornato.

Ripresa: UPDATE/DELETE e conflitti INSERT, poi DDL/type literals e supporto TimescaleDB, con misura PostgreSQL corpus L1/L2. TimescaleDB ancora non implementato; segnalazione parent links SEMI rewrite aperta. Priorità PostgreSQL → TimescaleDB. Demo non porting completo e nessun pacchetto pubblicato.

## Ripresa precedente: operatori JSON PostgreSQL verificati

Integrati due agenti (limite strumenti ma modifiche verificate centralmente): parser.json.cs, accessor/JSON operator routing e PostgreSQL extraction overrides; generators/postgres.json.cs e transforms per JSONExtract/Scalar e JSONBExtract/Scalar. Operatori ->, ->>, #>, #>>, chiavi letterali vs indici, concatenazioni, commenti e metadati confrontati con Python. Percorsi JSONPath/payload non supportati restano explicit NotPorted, non emissione approssimata. Non dichiarare JSON completo.

Coordinator ha aggiornato otto vecchi stub/error expectations e aggiunto 30 confronti end-to-end solo PostgreSQL (15 query × pretty), includendo CASE, DISTINCT ON, cast e filtri temporali. Test helper CompareSamples consente selezione dialetti per evitare fingere generator JSON base portato. Build centrale finale exit 0, 0 warning/errori; 645 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-json-central.

Demo pretty payload->>'device_id', cast(payload->>'value' AS DECIMAL) WHERE payload->>'name' ILIKE ... AND ts >= NOW()-INTERVAL '1 day' eseguita exit 0. Corpus esterno D:\clones\corpus immutato, nessun nuovo expected o esclusione; scoreboard completo non aggiornato. Ripresa: supporto PostgreSQL DML/DDL/type literals e misurazione corpus L1/L2; estensioni TimescaleDB ancora da implementare. Segnalazione parent links SEMI rewrite ancora da indagare. Priorità invariata PostgreSQL → TimescaleDB. Demo resta anteprima, non consegna completa.

## Ripresa precedente: VALUES e DISTINCT ON verificati

Integrati due agenti completati: parser.values.cs e generator.values.cs. Parser _parse_value/_parse_derived_table_values e due routing DISTINCT ON ora disponibili. Coordinator ha portato _values_to_select con closure builder locale copy=True e metadati sintetici Star di exp.select("*"); collegate CTE e set operations comprese Values in wrapper. Nessun bypass degli expected: trovata e corretta differenza di posizione Star con test Python.

Aggiunti 60 confronti end-to-end AST/SQL/error (15 query × base/PostgreSQL × pretty). SELECT DISTINCT ON senza proiezione resta esplicitamente NotPorted nel generatore e ha test separato; non dichiarato caso SQL passato. Vecchi stub tests aggiornati ai nuovi percorsi. Build finale soluzione exit 0, 0 warning/errori; 641 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-values-central.

Demo pretty WITH sample(device_id,value) AS (VALUES ...) SELECT DISTINCT ON(device_id) ... ORDER BY device_id,value DESC eseguita exit 0. Corpus esterno immutato; nessuna nuova registrazione e scoreboard non aggiornato. Parallelizzazione operativa senza copiare corpus. Restano JSON, DDL/DML, costruttori di tipi e altre dipendenze PostgreSQL; TimescaleDB non ancora implementato. Ripresa: parser/generatore JSON PostgreSQL in parallelo e measurement corpus L1/L2, poi estensioni TimescaleDB. Demo è anteprima, non porting completo.

## Ripresa precedente: CASE verificato, pattern predicates e DISTINCT

Chiuso il blocco condizionali rimasto aperto: CASE/IF parser+generator e test positivi CASE nei valori di intervallo, con suite verde. Integrati altri due agenti per LIKE/ILIKE/SIMILAR TO/ESCAPE e DISTINCT rendering; all/any rendering con quantifier-expansion mancante ancora esplicita. DISTINCT ON generazione verificata da AST Python; parsing ON resta NotPorted. Coordinator ha aggiunto 64 confronti pipeline reali e corretto _parse_type: type keywords senza literal ritornano colonne tramite retreat, necessario per colonna NAME PostgreSQL. Type-literal constructors/converters restano explicit NotPorted. Vecchi stub tests sostituiti da nuovi positivi/error parity, senza skip.

Verifica finale: build soluzione exit 0, 0 warning/errori; 629 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-pattern-central. Demo SELECT DISTINCT device_id, CASE WHEN name ILIKE 'sensor%' ... WHERE ts >= NOW() - INTERVAL '1 day' AND name NOT LIKE 'test%' eseguita pretty exit 0. Corpus esterno D:\clones\corpus immutato; scoreboard completo non aggiornato. Cloning parallelo continua a funzionare senza corpus duplicati.

Limiti: DISTINCT ON parsing, ANY/ALL parser closures/quantifier expansions, JSON/DDL/DML e altre parti PostgreSQL incomplete; TimescaleDB ancora non implementato. Segnalazione parent link SEMI rewrite da indagare resta aperta. Ripresa: misurare corpus PostgreSQL L1/L2 e portare gruppi più bloccanti; pianificare estensioni TimescaleDB per dimostrazione senza ridurre arbitrariamente compatibilità. Priorità PostgreSQL → TimescaleDB, non TSQL/Fabric.

## Ripresa precedente: funzioni finestra e aggregazioni FILTER

Integrati parser.windows.cs e generator.windows.cs da due agenti completati; coordinator ha collegato SELECT WINDOW clause (query modifiers ora supportano nodi o liste), aggiunto rendering FILTER/WITHIN GROUP/PER e 96 confronti pipeline (24 query × base/PostgreSQL × pretty). OVER, PARTITION, ORDER/frame ROWS/RANGE/GROUPS, exclusions disponibili nei percorsi testati; named windows e FILTER reali verificati. Gruppo generatore ha oltre 12.000 confronti Python. Vecchie aspettative di stub sostituite dai nuovi percorsi verificati, non da skip.

Verifica centrale: build soluzione exit 0, 0 warning/errori; 606 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-window-central. Corpus esterno immutato. Restano scope-aware relocation delle null wrappers e NULL-order simulation per target che non la supportano, oltre ad altri figli mancanti. Agente ha segnalato possibile mismatch parent link di join distaccato nella precedente rewrite SEMI PostgreSQL: non verificato centralmente in questo blocco, da indagare senza nascondere. Prossimo gruppo: CASE/IF parser+generator in parallelo. Priorità PostgreSQL → TimescaleDB; demo ancora anteprima.

## Ripresa precedente: intervalli PostgreSQL, parallelizzazione ripristinata

Due agenti indipendenti hanno portato parser.intervals.cs e generator.intervals.cs/generators/postgres.intervals.cs, integrati e verificati dal coordinatore. Parsing intervalli/spans/somme, stringhe con unità/frazioni, rendering base/PostgreSQL e conversione QUARTER→MONTH con mutazioni verificate contro Python. Chiusure parziali mantengono anchor zero e confini su funzioni speciali/brackets/CASE/type literals/numeri fuori runtime. Vecchie aspettative di stub aggiornate; aggiunti 48 confronti end-to-end parser+generator oltre ai gruppi agenti.

Verifica finale centrale: build soluzione exit 0, 0 warning/errori; 605 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-interval-central. Demo pretty DATE_TRUNC('hour', ts), COUNT(*) WHERE ts >= NOW() - INTERVAL '1 day' GROUP BY/ORDER BY eseguita exit 0. Corpus completo non rigenerato né scoreboard aggiornato.

Parallelizzazione ORA verificata: entrambi gli agenti hanno aperto D:\clones\corpus\L0.base.jsonl in sola lettura e confermato assenza di copia corpus nel workspace. Entrambi incarichi completati e applicati. I messaggi storici di cap copia >500MB sono superati. Corpus rimane esterno e immutato. README corretto: parser/generatore parziali e demo disponibili, non totalmente assenti. Ripresa: finestre OVER/ORDER/frame e altre dipendenze SQL PostgreSQL in parallelo; poi TimescaleDB. Priorità invariata, demo non consegna finale.

## Ripresa precedente: corpus esterno D:\clones\corpus

L'utente ha spostato l'intero corpus in D:\clones\corpus, fuori dall'albero SQLGlot. Verificati esistenza, assenza di corpus/ nel repository, dimensione e SHA256 immutato del L0.base condiviso. CorpusReader.RootDirectory contiene il percorso fisso di sviluppo; aggiornati tutti i lettori/test, default extractor e generator JSONPath. Nessuna configurazione multiutente introdotta. Reader condivide ancora L0 attraverso i due manifest e non scrive i dati.

Verifica centrale: build soluzione exit 0, 0 warning/errori; 591 test passati, 0 falliti/saltati, con -p:ArtifactsRoot=D:\Dev\BuildArtifacts\sqlglot-external-corpus. Accesso al corpus esterno confermato dai test. Le vecchie copie agenti erano state rimosse; repository senza corpus circa 23 MiB. Prossimo Agent.Run deve ancora confermare operativamente che usa il corpus esterno: non dichiarare questo test effettuato. Scoreboard: dotnet run --project src/Scoreboard -- D:\clones\corpus\v30.27.0. Le sezioni storiche con corpus/ interno sono superate da questa.

Ripresa: tornare alle funzionalità SQL (intervalli, finestre/funzioni PostgreSQL, poi TimescaleDB), senza ulteriore infrastruttura non necessaria. Priorità invariata PostgreSQL → TimescaleDB. Build/test temporali già verdi; demo rimane anteprima, non porting completo.

## Ripresa precedente: corpus in radice, L0 condiviso e suite verde

Tutta la cartella corpus spostata da src/SqlGlotSharp.Tests/corpus a corpus/ nella radice. L0.base.jsonl era già stato spostato dall'utente in radice; verificato contro SHA256 precedente, collocato in corpus/L0.base.jsonl. Una sola copia: entrambi i manifest v30.27.0 e v30.27.0-logger-context-full usano shared_files e shared_sha256. Nessuna riga expected cambiata e nessuna rigenerazione. CorpusReader.ResolveFile risolve il riferimento piatto al padre, con controllo nomi; aggiornati test, default extractor, generator JSONPath e comandi documentati. Script di spostamento una tantum rimosso dopo il successo.

Verifica finale: build soluzione exit 0, 0 warning/errori; 591 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-corpus-root. Nuovi test SHA256 identico per entrambe le versioni e rigetto traversal nei nomi condivisi. Dimensione repository esclusi bin/obj/.git/node_modules/.ralfai: 431,63 MiB, sotto il limite agenti 500 MiB. Riprovare Agent.Run al prossimo incarico indipendente: cap copia non ancora riverificato nel motore.

La suite verde comprende anche il blocco temporale rimasto aperto: EXTRACT, CURRENT_DATE, AT TIME ZONE, localtime/localtimestamp e PostgreSQL NOW/DATE_TRUNC, con 120 confronti AST/SQL/errori Python (30 query × 2 dialetti × pretty). WeekStart naming e numerosi altri percorsi temporali restano NotPorted. Prima di nuove funzionalità non ci sono test pendenti in questo blocco. Scoreboard completo non aggiornato. Priorità PostgreSQL → TimescaleDB; demo ancora anteprima. Ripresa: misurare coverage PostgreSQL L1/L2, poi intervalli/finestre/funzioni e TimescaleDB; evitare ulteriore infrastruttura non necessaria alla dimostrazione.

## Ripresa precedente: cast e tipi scalari verificati

Chiuso il blocco rimasto non verificato in chat: parser.types.cs (_parse_types/_parse_type_size/build_cast/_parse_cast), generator.types.cs, generators/postgres.types.cs, routing CAST/:: e dispatch specifico PostgreSQL. Tipi scalari, precisioni, fusi orari, array senza dimensione, default conversion error e collate disponibili nei percorsi confrontati. TypeCastPipelineTests esegue 132 confronti AST/SQL/error (33 query × base/PostgreSQL × pretty). Vecchie aspettative di stub aggiornate, errori dopo :: ora ParseError, IntDiv base verificato contro Python come CAST(x / y AS BIGINT).

Verifica centrale finale: build soluzione exit 0, 0 warning/errori; 589 test passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-types-central. Demo SELECT device_id::TEXT, CAST(value AS DECIMAL(10, 2)) FROM measurements WHERE value > 0 LIMIT 5 eseguita exit 0. Corpus completo non rieseguito; scoreboard storico non aggiornato. Stato corrente ora verde (supera il messaggio precedente con verifiche pendenti).

Limiti: type literals/constructors, UDT/pseudo types, nested/aggregate/enum/ref, format conversions, fixed-size cast accessors INT[3], custom type converters e type param bound adapters restano incompleti. Typed column constraints restano mancanti anche se _parse_types scalar ora disponibile. TRY_CAST speciale non collegato e PostgreSQL relativo transform mancante. Anchor delle nuove closure partial zero. Parallelizzazione ancora bloccata dalla copia >500MB. Ripresa: funzioni temporali e grammatica espressioni PostgreSQL, misurare production pipeline sul corpus, poi TimescaleDB. Priorità invariata PostgreSQL → TimescaleDB; demo è anteprima, non consegna finale.

## Ripresa precedente: slow-table ordinaria e JOIN USING

Portato parser.tables.cs con _parse_table_part/_parse_table_parts e continuazione slow _parse_table_slow, lasciando invariato fast path upstream. Nomi qualificati/quoted, ONLY, suffix *, funzioni tabella ordinarie, alias e USING ora disponibili; paginazione OFFSET/FETCH sbloccata. History/change/version/hints/schema/bracket/partition conservano confini NotPorted; nessuna dichiarazione di table parser completo. ParserJoinTests ora confronta USING realmente con Python senza monkey-boundary sulla slow table. Test vecchi di stub sostituiti da positivi e confini attuali. 45 input AST/error/production SQL nel gruppo CTE/pipeline, più ampio gruppo join/cursori base e PostgreSQL.

Build centrale exit 0, 0 warning/errori; suite 588 passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-tables-central. Demo SELECT device_id, COUNT(*) FROM measurements LEFT JOIN devices USING (device_id) GROUP BY device_id HAVING COUNT(*) > 1 LIMIT 5 eseguita exit 0. Corpus completo non rieseguito. Parallelizzazione ancora bloccata (>500MB).

Ripresa: cast e tipi PostgreSQL, poi funzioni temporali/TimescaleDB. Preprocessing QUALIFY, constraints, version/time travel e numerosi altri percorsi restano mancanti. Priorità PostgreSQL → TimescaleDB invariata; anteprima non porting completo.

## Ripresa precedente: elenchi colonne alias/CTE senza tipo

Portato parser.parameters.cs: _parse_function_parameter e closure limitata _parse_column_def per identificatori senza tipo; _parse_table_alias ora segue CSV/rparen/retreat upstream per colonne. Metodi partial anchor zero. Dichiarazioni typed/computed/constraints/ordinality restano NotPorted: ordinality non è nella metadata ColumnDef del baseline e attiva UNITTEST validation C#, non aggirata. Snapshot CONSTRAINT_PARSERS usata solo per confini dei callback, non dichiarata implementazione.

Confronti live Python ampliati a 28 input AST/SQL/error per alias e CTE, compresi commenti, liste vuote/trailing commas, derived-table e join alias. Vecchie aspettative di stub alias columns rimosse in favore dei nuovi positivi; L1 fixture usa typed INT realmente ancora mancante. Build centrale exit 0, 0 warning/errori; suite 586 passati, 0 falliti/saltati. ArtifactsRoot D:\Dev\BuildArtifacts\sqlglot-alias-central. Demo WITH totals(device,n) ... eseguita exit 0. Corpus completo non rieseguito.

Ripresa: slow-table/USING e cast PostgreSQL, poi estensioni TimescaleDB. Parallelizzazione rimane bloccata dalla copia >500MB. Priorità invariata. Nessun modulo parser/generator completo e nessuna consegna finale.

## Ripresa precedente: parsing WITH/CTE e pipeline completa per CTE ordinarie

Portato direttamente parser.ctes.cs (_parse_with/_parse_cte partial zero anchors) e ramo CTE di _parse_select_query: merge inner WITH, recursive flag, wrapper unnest, diagnostica statement mancante. CTE ordinarie/multiple/annidate, RECURSIVE e MATERIALIZED/NOT MATERIALIZED verificati con 16 input Python AST/error/SQL; sei test di confini espliciti incluse alias columns, VALUES, SEARCH/CYCLE, USING KEY e malformed inputs che dipendono da alias-column parsing. Non dichiarare queste varianti complete. Test storici che richiedevano stub WITH aggiornati; corpus non escluso né modificato.

Build centrale exit 0 con 0 warning/errori; suite 586 passati, 0 falliti/saltati. Usato `-p:ArtifactsRoot=D:\Dev\BuildArtifacts\sqlglot-cte-central`. Demo eseguita exit 0 per WITH totals AS (SELECT device_id, COUNT(*) AS n FROM measurements GROUP BY device_id) SELECT totals.device_id, totals.n FROM totals WHERE totals.n > 2 LIMIT 5.

Agent.Run NON disponibile in questo blocco: copia progetto 675 MB supera limite 500 MB (nessun file eliminato per aggirarlo). Parallelizzazione richiede intervento su esclusioni/cap nell'engine Ralf, non presumere attiva alla prossima ripresa. Priorità PostgreSQL → TimescaleDB invariata. Ripresa: slow table/USING, alias columns, cast PostgreSQL, corpus L1/L2 production pipeline. TimescaleDB non ancora portato; anteprima non consegna completa.

## Ripresa precedente: JOIN e generazione WITH/CTE

Integrati parser.joins.cs e generator.joins.cs/generator.ctes.cs. Parser: ordinary JOIN ON, lati/kind, comma joins e ricorsione; USING completo su derived-table nei test, ma plain-table JOIN u USING(...) rimane bloccato dalla slow-table branch. Nessun allargamento artificiale del fast path. Generatore: join_sql, hint_sql/joinhint_sql, with_sql/cte_sql, comment mutation, recursive/materialized CTE e opzioni verificati contro Python (1.000 casi nel gruppo generatore). Parsing WITH resta da portare: generazione CTE da AST disponibile NON equivale alla pipeline SQL WITH completa.

Verifica centrale: build soluzione exit 0, 0 warning/errori; suite 581 passati, 0 falliti/saltati, con `-p:ArtifactsRoot=D:\Dev\BuildArtifacts\sqlglot-join-central`. Demo eseguita exit 0 per SELECT t.a, COUNT(*) FROM measurements t LEFT JOIN devices d ON t.device_id = d.id GROUP BY t.a HAVING COUNT(*) > 1 LIMIT 3. Vecchie aspettative di stub aggiornate, nessun caso corpus escluso. Corpus completo non rieseguito. Ripresa: parser WITH/CTE e slow-table dependency per USING, poi cast PostgreSQL. TimescaleDB ancora non implementato. Priorità invariata PostgreSQL → TimescaleDB.

## Ripresa precedente: prima pipeline PostgreSQL eseguibile (anteprima)

Integrati preprocessing condizionale del generatore e catena Select PostgreSQL: eliminate_semi_and_anti_joins implementata nei percorsi testati, QUALIFY esplicitamente NotPorted; relocation CTE/ensure_bools mancanti non bypassate. Parser: star/COUNT e DISTINCT arguments nei percorsi supportati. Nuovi confronti Python su SQL/AST mutation/parent links; vecchie aspettative di stub sostituite dai risultati ora disponibili. Caso CTE aggiunto dall'agente non era supportato: spostato a test esplicito di dipendenza with_sql, non dichiarato passato.

Aggiunto src/SqlGlotSharp.Demo alla soluzione: prima anteprima nativa SQL PostgreSQL → AST → SQL, senza Python di produzione né connessione database. Eseguito esempio SELECT a, COUNT(*) FROM measurements WHERE a > 2 GROUP BY a HAVING COUNT(*) > 1 LIMIT 3 con exit 0. Comandi in USAGE.md. Non è consegna finale: JOIN/CTE/cast/funzioni speciali e numerosi altri percorsi restano mancanti; TimescaleDB non iniziato. Questo stato supera le vecchie dichiarazioni sotto di pipeline totalmente assente.

Verifica centrale finale: build soluzione exit 0, 0 warning/errori; 569 test passati, 0 falliti/saltati. Usato `-p:ArtifactsRoot=D:\Dev\BuildArtifacts\sqlglot-pipeline-central` per lock PortingTools standard. Corpus completo non rieseguito: scoreboard storico non misura i nuovi percorsi. Ripresa: completare JOIN/CTE e casting PostgreSQL e ampliare corpus L1/L2 con production generate, mantenere priorità PostgreSQL → TimescaleDB.

## Ripresa precedente: GROUP/HAVING e generazione set operations

Integrati due incarichi separati: parser.group.cs porta GROUP BY/HAVING, CUBE/ROLLUP/GROUPING SETS con dipendenze espressioni ancora parziali (anchor zero); generator.setoperations.cs porta keyword UNION/INTERSECT/EXCEPT, assembly iterativo, precedenze/modificatori/commenti e subquery_sql. set_operations resta parziale per _move_ctes_to_top_level. PostgreSQL SELECT transform/preprocessing restano bloccati: i test dei keyword/operandi non-query NON equivalgono a query PostgreSQL complete. Verificati risultati contro Python, inclusa modalità loose precedence aggiunta dal coordinatore. Aggiornate cinque aspettative obsolete di NotPorted, conservando i confini realmente mancanti.

Build centrale soluzione exit 0, 0 warning/errori; suite principale 554 passati, 0 falliti/saltati. Output separato `-p:ArtifactsRoot=D:\Dev\BuildArtifacts\sqlglot-group-central` per evitare lock standard. Corpus completo non rieseguito, scoreboard precedente invariato. Priorità PostgreSQL → TimescaleDB. Ripresa consigliata: JOIN/CTE e soprattutto preprocessing/SELECT PostgreSQL per sbloccare pipeline pubblica; COUNT/star restano confini espliciti. Nessun modulo parser/generator dichiarato completo.

## Ripresa precedente: gruppo set operations di parser.py

Integrato parser.setoperations.cs: _parse_set_operations, _parse_wrapped e _parse_wrapped_csv con anchor aggiornate; parse_set_operation resta parziale (anchor zero) per VALUES canonicalization e default nullable non disponibili. UNION/INTERSECT/EXCEPT, precedenza, DISTINCT/ALL, annidamento, modificatori e commenti verificati contro Python per base/PostgreSQL. Aggiornati tre vecchi test che pretendevano ancora NotPorted per UNION; i confronti positivi coprono questi percorsi. Nessuna modifica al generatore e nessuna dichiarazione di parser completo.

Verifica centrale: build soluzione exit 0, 0 warning/errori; suite principale 537 passati, 0 falliti/saltati. Build standard bloccata dal processo PortingTools PID 7280; usata directory separata senza interrompere il processo: `-p:ArtifactsRoot=D:\Dev\BuildArtifacts\sqlglot-setops-central` sia per build sia per test. Corpus completo non rieseguito, vecchio scoreboard non aggiornato. Priorità PostgreSQL → TimescaleDB. Prossimo blocco: dipendenze query mancanti (JOIN/GROUP/CTE) e generatore per pipeline PostgreSQL; procedere per gruppi del modulo originale e conservarne la copertura esplicita.

## Ripresa precedente: inventario per modulo originale Python

L'utente richiede avanzamento misurato per modulo originale, non per quantità di file C#. Aggiunto `port_status.py --modules` (anche `--json`), con conteggi per stato e file C# associati. La copertura delle anchor NON certifica completezza funzionale: classi/tabelle generate possono essere coperte senza algoritmi completi. Nessun accorpamento fisico eseguito e nessun membro SQL aggiunto in questo blocco.

Inventario eseguito con exit code 0: parser.py 192 anchor ported, 338 missing, 92 stub; generator.py 227 ported, 409 missing, 13 stub; parsers/postgres.py 12 ported, 17 missing, 9 stub; generators/postgres.py 38 ported, 29 missing, 1 stub. Questi numeri includono attributi e dichiarazioni, non sono percentuali di funzionalità. Aggiunti due test Python dell'aggregazione. Build soluzione riuscita con 0 warning/errori. Priorità invariata PostgreSQL → TimescaleDB. Ripresa: usare questo inventario per completare gruppi coerenti nei moduli parser/generatore; mantenere esplicite le dipendenze e non dedicare blocchi a TSQL/Fabric.

Comando: `dotnet run --project src/PortingTools --no-build -- src/tools/port_status.py --modules`.

## Ripresa precedente: provenienza del logger nel recorder e replay

Recorder aggiornato a v5: cattura i metodi del logger effettivo (anche mock dell'oggetto o di un singolo metodo), conserva `logger_context`, ripristina i metodi dopo la cattura e distingue logging normale/disabilitato/mock. Replay L1 confronta warnings normali e chiamate mock secondo contesto esplicito; contesti sconosciuti restano NotPorted. Nessun warning legacy soppresso e nessun expected modificato. Nuovi test live sui tre percorsi problematici nei contesti mock/normale/disabilitato, più tooling Python.

Verifica centrale: build soluzione **0 warning/errori**; suite principale **527 passati, 0 falliti, 0 saltati**. Corpus completo NON rigenerato: i tre mismatch legacy restano visibili e i conteggi precedenti non rappresentano una misura del nuovo corpus. Prossimo blocco: rigenerazione in directory nuova con ambiente oracle isolato, verificare manifest e scoreboard prima di sostituire corpus attuale. Comando extractor: `python -B src/tools/extract_corpus.py --upstream python --output corpus/v30.27.0-logger-context-full --log D:\Dev\BuildArtifacts\sqlglot-csharp\logs\oracle-logger-context-full.log` (usare Python dell'ambiente isolato predisposto, non presumere dipendenze globali). Pipeline finale non disponibile; priorità PostgreSQL → TimescaleDB.

## Ripresa precedente: BaseParser e funzioni senza parentesi

Portato `parsers/base.cs`, collegata factory base a BaseParser e ramo NO_PAREN_FUNCTIONS in parser.functions.cs. LOCALTIME/LOCALTIMESTAMP rimangono specifici del BaseParser, non aggiunti al Parser generico. Tre nuovi test includono 786 confronti Python (base/generico/Postgres), commenti, errori e cursori. Aggiornate due vecchie aspettative di stub per CURRENT_DATE, ora verificato nel nuovo gruppo.

Verifica centrale: build soluzione **0 warning/errori**; suite principale **526 passati, 0 falliti, 0 saltati**. Scoreboard completo invariato nel totale: **26.499 casi — 9.110 passati, 3 falliti, 17.386 non portati**. Risolti i due mismatch LOCALTIME e sbloccati altri 29 casi; nessun caso corpus escluso. Report: `D:\Dev\BuildArtifacts\sqlglot-csharp\reports\baseparser-central`. Restano tre differenze di logger mockato nei test originali Python: correggere registratore preservando il contesto, senza sopprimere log nel replay né alterare expected a mano. Poi ampliare parser PostgreSQL. Pipeline finale non disponibile; priorità PostgreSQL → TimescaleDB.

## Ripresa precedente: diagnosi L1 e correzione DATE_TRUNC

Investigati tutti i 16 mismatch: 11 derivavano dalla normalizzazione DateTrunc mancante; 2 LOCALTIME/LOCALTIMESTAMP dalla factory base che usa Parser anziché BaseParser upstream; 3 sono differenze di contesto del logger mockato nei test Python (AST e log normali C#/Python coincidono). Nessun expected modificato e nessun caso escluso.

Portato DateTrunc.__init__ in `temporal.datetrunc.cs`, modificati solo i suoi costruttori generati e `gen_csharp.py` per conservarlo alla rigenerazione. 728 confronti live Python su AST, parent e metadata; test di emissione del generatore. Aggiunto anche `L1MismatchRegressionTests.cs` per la diagnosi dei 16 originali.

Verifica centrale: build soluzione **0 warning/errori**, suite principale **525 passati, 0 falliti, 0 saltati**. Scoreboard completo: **26.499 casi — 9.079 passati, 5 falliti, 17.415 non portati**. Report: `D:\Dev\BuildArtifacts\sqlglot-csharp\reports\datetrunc-central`. Risolti 11 mismatch senza esclusioni. Prossimo blocco: portare BaseParser e ramo NO_PAREN_FUNCTIONS/factory base per i 2 mismatch LOCALTIME; poi preservare nel registratore il contesto logger mockato dei 3 casi senza sopprimere warnings nel replay. L1 non superato e pipeline finale non disponibile. Priorità PostgreSQL → TimescaleDB.

## Ripresa precedente: replay L1 e prima misura completa

Integrati `CorpusVerifier` dispatch L1 e `L1Replay.cs`: parse/parse_one base e PostgreSQL, Block multi-statement, opzioni disponibili, warnings, identità parser e normalizzazione UTF-16 dei metadati AST (anche payload t). parse_into, opzioni/identità non disponibili e grammatica mancante restano NotPorted. Aggiunti 11 test, inclusi 99 confronti con Python/extractor e fixture che distinguono mismatch da NotPorted.

Verifica centrale: build soluzione **0 warning/errori**; suite principale **521 passati, 0 falliti, 0 saltati**. Eseguito scoreboard sull'intero corpus di **26.499 casi**: **9.068 passati, 16 falliti, 17.415 non portati**. Report e dettagli: `D:\Dev\BuildArtifacts\sqlglot-csharp\reports\l1-initial\scoreboard.md` e `failures.json`. Il successo del comando indica generazione del report, NON superamento del gate L1: ci sono 16 differenze reali da investigare e numerosi stub. Prossima azione: leggere failures.json e correggere i mismatch con test di regressione, poi ordinare le dipendenze mancanti del parser per copertura PostgreSQL. Pipeline finale non disponibile; priorità PostgreSQL → TimescaleDB.

## Ripresa precedente: factory del parser PostgreSQL

Collegata factory per tipo esatto `postgres.Postgres` in `dialect.parse.cs` e aggiunto `parsers/postgres.dispatch.cs`: overlay delle tabelle callable con confini NotPortedException per callback mancanti, anchor parziali zero. Testati SELECT comuni, identificatori quotati, stringhe, placeholder ?, $1 e :name, operatori ^/#/~, bitwise, concatenazione, predicati disponibili e ordinamento NULL. 48 confronti Python su AST/errori. Restano esplicitamente non portati %s/%(name)s, callback speciali, JSON, regex, &&/@@, cast, star e statement speciali.

Verifica centrale: build soluzione **0 warning/errori**; suite principale **510 passati, 0 falliti, 0 saltati**. Corpus completo non rieseguito e L1 non ancora collegato. Prossimo blocco: replay L1 con identità parser, options e normalizzazione UTF-16 corretti, per misurare i percorsi reali. Parser PostgreSQL non completo; pipeline finale non disponibile. Priorità PostgreSQL → TimescaleDB.

## Ripresa precedente: collegamento parsing del dialetto base

Creato `dialects/dialect.parse.cs`: `Dialect.parse(string sql, ErrorLevel? error_level)` e factory `parser`, per il solo tipo esatto base Dialect. Riusa tokenizer e Parser.parse; sottoclassi e PostgreSQL sono rifiutati esplicitamente con NotPortedException finché la factory specifica non sarà portata. API kwargs parziale documentata, anchor hash zero. Tre nuovi test includono 45 confronti Python di AST/errori, multi-statement e rifiuto dei dialetti non supportati.

Verifica centrale: build soluzione **0 warning/errori**; suite principale **491 passati, 0 falliti, 0 saltati**. L1 corpus non ancora collegato né misurato. Il primo agente del blocco PostgreSQL/L1 ha raggiunto il limite degli strumenti senza modifiche integrabili; il secondo incarico delimitato base è stato integrato e verificato. Prossimo blocco: factory PostgreSQL con override mancanti espliciti, quindi replay L1 e normalizzazione UTF-16. Pipeline completa non disponibile; priorità PostgreSQL → TimescaleDB.

## Ripresa precedente: entrypoint del parser

Creato `parser.entrypoints.cs`: `Parser.parse`, `_parse` e ciclo batch con splitting sui semicolon, reset e gestione degli errori upstream. Dispatch `_parse_statement` parziale e dichiarato con hash zero: SELECT/espressioni usano i percorsi esistenti, statement speciali/command/WHILE mancanti restano `NotPortedException`. Aggiunti 5 test con confronti Python di AST, commenti, cursore, chunk, errori e riuso del parser.

Verifica centrale: build soluzione **0 warning/errori**; suite principale **488 passati, 0 falliti, 0 saltati**. Corpus SQL completo non rieseguito. Non è ancora una pipeline pubblica di transpiling: mancano preprocessing PostgreSQL e numerosi percorsi SQL. Prossimo blocco: collegare parse ai dialetti e valutare i risultati L1 sul corpus, mantenendo espliciti i limiti. Priorità PostgreSQL → TimescaleDB.

## Ripresa precedente: subquery nei predicati IN

Portati `Query.subquery`, `maybe_copy` e `to_identifier` in `expressions/query.subquery.cs`. Il parser usa ora `.subquery(copy: false)` nel ramo IN: rimosso lo stub relativo a questa dipendenza. Test contro Python per alias stringa/nodo, copia, collegamenti padre–figlio, IN con SELECT anche annidati, commenti, cursore e diagnostiche.

Verifica centrale dopo integrazione: build soluzione riuscita, **0 warning/errori**; suite principale **483 passati, 0 falliti, 0 saltati**. Corpus SQL completo non rieseguito. Restano UNNEST, set operations, tipi, accessori e pipeline pubblica SQL incompleti. Prossimo blocco: entrypoint del parser e relative dipendenze, poi preprocessing PostgreSQL. Priorità PostgreSQL → TimescaleDB invariata. Questo aggiornamento supera i limiti e conteggi storici sotto, in particolare il vecchio blocco su IN-subquery.

## Ripresa precedente: predicati del parser

Integrato `parser.predicates.cs`: `IS`/`IS NOT`, `DISTINCT FROM`, `IS JSON`, `BETWEEN` con SYMMETRIC/ASYMMETRIC e `IN` su liste, brackets e field. Routing aggiornato in `parser.operators.cs`; aggiunti 12 test con confronti Python su AST, cursore, commenti e diagnostiche. Aggiornate le vecchie asserzioni che pretendevano ancora uno stub per IS/BETWEEN/IN.

Verifica del coordinatore: build soluzione riuscita, 0 warning/errori; suite `SqlGlotSharp.Tests`: **477 passati, 0 falliti, 0 saltati**. Non è una nuova esecuzione del corpus SQL completo.

Limite: `IN (SELECT ...)` segnala ancora `NotPortedException` perché manca `Query.subquery(copy=False)`. Prossimo blocco: portare questa dipendenza, poi completare entrypoint SQL e dipendenze PostgreSQL. UNNEST, set operations, tipi e funzioni speciali rimangono incompleti. Priorità invariata: PostgreSQL → TimescaleDB. Questo aggiornamento prevale sui conteggi storici sotto.


> **Leggere questo file per primo quando si riprende il lavoro.** È una fotografia aggiornata nella sessione in cui è stato scritto, non il vecchio registro cronologico. Va aggiornato dopo ogni blocco verificato. Le prove riportate sotto sono state rieseguite prima della scrittura.

## 1. Obiettivo e ultime decisioni dell'utente

Convertire il progetto Python SQLGlot in una libreria **C# / .NET 10**, con algoritmi e comportamento confrontabili con Python. Non utilizzare Python come backend della libreria di produzione e non sostituire il porting fedele con un parser semplificato senza autorizzazione.

**Priorità approvata, che supera l'ordine storico del piano:**
1. Pipeline base necessaria a SQLGlot.
2. **PostgreSQL**: parsing, generazione e transpiling funzionanti e verificati.
3. **TimescaleDB**, subito dopo PostgreSQL: nuovo dialetto derivato da Postgres, con test dedicati.
4. **TSQL e Fabric rinviati**: preservare codice e test già realizzati; non dedicarvi nuovi blocchi prima dei target prioritari.
5. Kusto/KQL non prioritario.

TimescaleDB non è presente nel baseline Python. Non fingere che esista già un oracolo upstream per quel dialetto.

L'utente non sta revisionando il codice e non ha al momento le competenze per farlo. Ha chiesto autonomia, blocchi di lavoro più ampi e feedback sul **risultato finale eseguibile**, non approvazioni tecniche a ogni incremento. Non chiedergli di verificare implementazioni o accettare silenziosamente fallimenti. Non promettere esecuzione in background dopo la fine della sessione.

**Parallelizzazione verificata nella sessione di ripresa:** avviati due agenti indipendenti per fondamenti di parser e generatore. Entrambi hanno raggiunto il timeout di 15 minuti, lasciando un file ciascuno; i file sono stati integrati, letti e verificati dal coordinatore con cinque nuovi test. Non risultano agenti ancora in esecuzione. Il timeout senza report/test impone revisione e verifica centrale: non considerare automaticamente completato un incarico.

## 2. Stato funzionale: non è ancora il prodotto finale

**Non è disponibile una pipeline SQL → AST → SQL né il transpiling SQL completo.** Nessun pacchetto è stato pubblicato e nessun global tool è stato installato.

| Area | Situazione reale |
|---|---|
| Tooling e corpus | Operativi: dipendenze, hash/anchor, generazione, registratore, harness e scoreboard |
| Tokenizer | Funzionante per base, PostgreSQL, TSQL, Fabric e JSONPath; gate L0 passato |
| AST | Gerarchie/costruttori generati, collegamenti padre–figlio, traversals, ricerca, mutazioni, copia, trasformazioni, hashing/uguaglianza, commenti e metadati; **non tutti i metodi Python sono portati** |
| Serde | Caricamento/dump AST; gate di round-trip L1 passato. Non equivale a parsing SQL C# |
| Dialetti | Registry, impostazioni/versione, plugin, tokenizer factory, normalizzazione/quoting Unicode, formati temporali e conversione JSONPath implementati; restano helper e deleghe dipendenti da parser/generatore |
| JSONPath | Parser nativo confrontato con Python su 531 casi di AST/errori |
| Typing | Metadati base di 294 classi: 164 return types, 130 callback; due helper e contratto ITypeAnnotator. **L'annotatore optimizer vero non è implementato** |
| Parser SQL | Fondamenta del cursore in `parser.cs`; `parser.validation.cs` aggiunge expression, validate_expression, raise_error/check_errors, _try_parse e _find_sql. Tabelle callable e parsing delle istruzioni ancora da implementare |
| Generatore SQL | Tabelle dati generate; `generator.cs` aggiunge opzioni/stato parziali, diagnostica unsupported, generate e helper di formattazione. Dispatch parziale in `generator.dispatch.cs` e SQL dei nodi elementari disponibili, anche annidati; preprocess, trasformazioni e numerosi handler restano NotPorted; nessuna generazione SQL completa |
| PostgreSQL completo | Tokenizer disponibile; parser/generatore specifici da portare dopo il base |
| TimescaleDB | Nessuna implementazione ancora |

La stima comunicata in chat era **circa 25% (20–30%) rispetto al vecchio piano completo**, non una misura automatica e non una data di consegna. Dopo il cambio di priorità non è stata ricalcolata. Non usare quantità di codice generato, gate intermedi o percentuale di test come percentuale di prodotto finito.

## 3. Ultime verifiche effettivamente eseguite

Dalla radice `D:\clones\sqlglot`:

```powershell
dotnet build src/SqlGlotSharp.slnx
dotnet test src/SqlGlotSharp.slnx --no-build --logger "console;verbosity=minimal"
dotnet run --project src/Scoreboard -- corpus/v30.27.0
```

Risultati nella sessione di scrittura:
- **Build exit 0**, 0 errori, 0 warning.
- **467 test della soluzione principale passati**, 0 falliti, 0 saltati (ultima ripresa: build exit 0, 0 errori/warning; suite completa confermata dal coordinatore).
- Ultimo blocco: `parser.queries.cs` e parentheses supportano SELECT base preparati su cursore, proiezioni/alias, tabelle fast-path, WHERE, ORDER, LIMIT/FETCH/OFFSET e subquery nei percorsi disponibili. Oracle query e parentesi verdi; corretto ordine kwargs di Select per serde. Nessun entrypoint parse/statement completo. IS/BETWEEN, slow table (anche FROM t seguito direttamente da OFFSET/FETCH), star parsing, joins/CTE/group/window/setops e tipi restano NotPorted. Le fixture nuove di equivalenza usano solo percorsi disponibili; quattro regressioni separate verificano limiti IS/BETWEEN/OFFSET/FETCH, nessun golden corpus cambiato. PREWHERE test usa token esplicito; nome tabella non quotato riservato corretto in tbl. Tutte le query anchor incomplete restano zero.
- PostgreSQL: `generators/postgres.handlers.cs` porta sette handler e sei trasformazioni inline, incluso placeholder %(name)s, bitwise XOR # e regex, verificati con 6.864 confronti Python; sette hash completi e TRANSFORMS parziale zero. SELECT PostgreSQL/Xor/preprocess restano NotPorted. Agente parser al limite: coordinatore ha corretto oracle e integrazione; agente PostgreSQL completato. Suite totale 467/467. Nessun agente attivo. Prossimo: range predicates e slow tables, quindi entrypoint SQL e preprocessing PostgreSQL; non dichiarare pronto transpiling.
- Ultima integrazione: `parser.functions.cs` e columns implementano percorsi ordinary Anonymous/registry con oracle cursore/AST/commenti/metadata/errori; special grammar/window/lambda/custom builder restano parziali espliciti. UNKNOWN è anche token tipo: fixture generiche rinominate mystery per testare nomi non-tipo e aggiunto test specifico ABS(UNKNOWN(1)) che verifica _parse_types ancora NotPorted; nessun golden corpus alterato. `generator.functions.cs` implementa anonymous_sql, function_fallback_sql, function_name, func, format_args e dispatch; trasformazioni/override PostgreSQL non portati restano prioritari espliciti. Gli agenti erano al limite con verifiche incomplete: coordinatore ha corretto fixture e vecchi limiti e confermato 456/456. SELECT base con Anonymous ora renderizza; generate/preprocess e SELECT PostgreSQL ancora bloccati. Nessun agente attivo. Prossimo blocco parsing query SELECT/table/clausole, senza dichiarare completati tipi o funzioni speciali.
- Ultima integrazione: `parser.parentheses.cs` e primary supportano parentesi aritmetiche annidate e tuple con oracle AST/commenti/posizioni/errori/cursore; query SELECT/WITH/FROM/VALUES/SUMMARIZE/DESCRIBE e dipendenze profonde restano NotPorted. Agente parser al limite con 7/8 test; coordinatore ha corretto fixture SUMMARIZE (token esplicito) e vecchie aspettative sulle parentesi vuote (ora ParseError upstream), verificando suite totale. `generator.collections.cs` porta star_sql/tuple_sql, due anchor complete e 3.456 confronti Python (4 nuovi test). SELECT base con grouping-set tuple e star ora supportati; PostgreSQL SELECT resta bloccato dal transform, generate dal preprocess. Nessun agente attivo. Prossimo blocco: parser chiamate funzione ordinarie, poi query SELECT; non ripetere porting parentesi già presente.
- Ultimo blocco: `generator.select.cs` aggiunge assembly SELECT base e helper GROUP/FETCH/LIMIT/OFFSET/query modifiers (16 anchor, select/limit/offset/simplify parziali zero). Test Python per SELECT base e helper, incluse mutazioni TOP/copia, sono verdi. Dispatch PostgreSQL SELECT resta esplicitamente NotPorted per preservare il transform upstream non ancora portato (QUALIFY elimination); non eredita silenziosamente SELECT base. Star/CTE/distinct/functions/rewrite e preprocess ancora mancanti. Il lavoro agente parser su funzioni/parentesi aveva sei errori di compilazione e nessun test: **scartato, non integrato**; quei metodi restano al precedente confine NotPorted. Il generatore agente era incompleto: il coordinatore ha corretto transform precedence, test obsoleti e harness, poi verificato 412/412. Nessun agente attivo. Prossima ripresa: ripartire dal gruppo parser funzioni/parentesi con scope ridotto e test, poi query parsing e preprocessing PostgreSQL.
- Ultima integrazione: `parser.columns.cs` e routing operators supportano colonne ordinarie/quotate/qualificate anche in operatori, nomi oltre quattro parti come Dot, fallback placeholder e stringhe adiacenti; tipi/funzioni/accessori/parentesi restano espliciti. 13 nuovi test oracle colonne. `generator.clauses.cs` porta helper liste e clausole/table con 4 nuovi test e confronti Python, ordered_sql resta parziale per simulazioni NULL ordering. Agenti non hanno concluso una suite totale verde: il coordinatore ha aggiornato aspettative obsolete dei limiti, aggiunto verifiche positive sui nuovi atomi e confermato 407/407. Errori operand mancanti ora ParseError upstream; DIV alfabetico senza operand conserva il fallback upstream con retreat. Nessuna pipeline SELECT completa o preprocess: prossimo blocco funzioni/parentesi e query base.
- Ultima integrazione: `parser.operators.cs` porta il gruppo di precedenze su percorsi literal-only (aritmetica, booleani, comparazioni, bitwise, suffissi null, AT TIME ZONE); tutti i 22 membri mantengono zero hash per dipendenze mancanti (tipi/colonne/funzioni/parentesi/range/accessori). `generator.operators.cs` aggiunge operatori tramite dispatch reale, incluso rendering iterativo dei connector e commenti/pretty; 35 anchor locali verificate aggiornate, div/dpipe/is restano parziali. Il parser agente completato, generatore al limite: il coordinatore ha corretto anchor e test obsoleto Add e rieseguito l'intera suite. Test oracle verdi non equivalgono a entrypoint SQL: nessun parse completo/preprocess ancora. Script scoped `src/tools/sync_operator_anchors.py` rigenera solo le anchor operatori esplicitamente verificate, lasciando tre parziali zero; non usarlo per promuovere nuove implementazioni senza revisione.
- Ultimo blocco: `parser.primary.cs` introduce dispatch stringhe/numeri e gruppo primary/placeholder/parametri/variabili, con oracle su AST/errori/commenti/cursore. Parentesi con espressioni, parametri sessione e star ops restano NotPorted; le anchor incomplete sono zero. `generator.dispatch.cs` collega gli handler disponibili a `Generator.sql`, inclusi commenti e dispatch exact-type; nested AST supportati ora renderizzano realmente. Trasformazioni/handler/fallback mancanti e override placeholder PostgreSQL restano espliciti. `generate` resta bloccato da preprocess. Tre vecchi test aspettavano NotPorted anche per nodi ora supportati: aggiornati ad asserire SQL esatto, mantenendo i controlli sui limiti veri. Agente parser completato, agente generatore al limite con lavoro verificato e integrato; nessun agente in background.
- Ultima integrazione parallela: `parser.placeholders.cs` aggiunge solo `_parse_string_as_identifier` con adattatore ristretto documentato e 9 test (oracle su 18 cursori ripetuti); **non implementa placeholder**. `generator.simple.cs` aggiunge column_parts/column_sql, var/parameter/placeholder/paren/neg/alias SQL, con 6 test inclusi confronti Python. Tutte le anchor nuove verificate dagli agenti. Nessun dispatch/preprocess/entrypoint SQL aggiunto; i test dei figli usano rendering controllato. Prossimo lavoro parser: risolvere il gruppo ciclico primary/placeholder/parameter tramite porting coordinato, anziché ripetere richieste di sole leaf bloccate dalle stesse dipendenze. Comprendono test C#, fixture oracle e integrazioni con gli strumenti Python; non sono test della pipeline SQL completa.
- Ultimo blocco integrato con due agenti completati: `parser.literals.cs` porta `_advance_any`, `_parse_csv`, `_identifier_expression` (9 test); `generator.leaves.cs` porta identifier/literal/null/boolean SQL, escape_str e sostituzione line break (10 test con oracle Python). Dispatch SQL e preprocess invariati e non portati. I parser generali dei letterali richiedono ancora le tabelle callable e dipendenze placeholder/star; non considerarli implementati. Tutte le nuove anchor sono state verificate dagli agenti con port_status; build e suite totale confermate dal coordinatore.
- Scoreboard su **26.499 casi**: **7.323 passati, 0 falliti, 19.176 non portati**.
- I 7.323 passati sono L0 (tokenizzazione). Gli altri livelli L1/L2/L3 restano NotPorted nella verifica SQL.
- Un precedente run xUnit dell'intero L0 ha eseguito e superato tutti i 7.323 casi senza skip. Nella presente sessione è stato rieseguito lo scoreboard, non quel run xUnit separato.
- Corpus completo registrato con recorder **v4**: 1.558 test Python passati, 26.499 casi in 16 JSONL.
- Serde: tutti gli output AST nei **7.855 record L1** completano il round-trip (7.799 output registrati, incluse uscite null; 591 class tag distinti). Questi AST sono prodotti da Python.
- Confronti AST: 44 confronti catturati da tre test di uguaglianza upstream passano in C#.
- Round-trip L2 aggiuntivo: 7.839 input supportati su 7.841; due classi test-local `SpecialUdf` sono esplicitamente non caricabili, anche da Python `serde.load`. Non sono fallimenti SQL accettati né un gate L2 passato.
- JSONPath: 531 casi Python di AST/errori passano.
- Unicode: 2.947 casi oracle di casing/proprietà passano, con tabelle **Unicode 14.0.0** del Python utilizzato; PostgreSQL mantiene la sua normalizzazione ASCII-only.
- Builder dei dialetti: 34 output Python confrontati con C#.

**Rieseguire le verifiche al rientro**: i file o l'ambiente potrebbero essere cambiati dopo questo snapshot.

## 4. Percorsi, progetti e strumenti

- Sorgenti Python read-only: `python/`.
- Specifiche: `csharp/AGENTS.md` e `csharp/PORTING_PLAN.md`. L'override di priorità PostgreSQL → Timescale è ora registrato anche nel piano.
- Baseline: SQLGlot `v30.27.0-1-g5c101a25e`, commit abbreviato `5c101a25e`.
- `UPSTREAM.json` contiene ancora una descrizione di stato bootstrap obsoleta: **non usarla come stato del porting**. Per i dettagli dell'oracolo controllare anche il MANIFEST del corpus.
- Soluzione: `src/SqlGlotSharp.slnx`.
- Libreria: `src/SqlGlotSharp/`.
- Test principali: `src/SqlGlotSharp.Tests/`.
- Lettore/runner/verifier condivisi: `src/SqlGlotSharp.Corpus/`.
- Test SQL per caso: `src/SqlGlotSharp.CorpusTests/`, **fuori dalla soluzione principale**. Il run non filtrato fallisce attualmente sui livelli non portati: non confonderlo con un guasto dei test principali.
- Scoreboard: `src/Scoreboard/`.
- Bridge di sviluppo che lancia Python: `src/PortingTools/`.
- Tooling Python: `src/tools/` (`closure.py`, `port_status.py`, `gen_csharp.py`, `extract_corpus.py` e generatori/recorder aggiuntivi).
- Corpus completo: `corpus/v30.27.0/`; gli altri corpus in quella cartella sono incrementi/regressioni, non sostituzioni della suite completa.
- Configurazione operativa: `ops/`; registrazione oracle tramite `ops/run-oracle.ps1` e `ops/oracle-requirements.txt`.
- Build, intermedi, log e report esterni: `D:\Dev\BuildArtifacts\sqlglot-csharp\`.
- Oracle venv esterno: `D:\Dev\BuildArtifacts\sqlglot-csharp\oracle-venv\Scripts\python.exe`.
- Python 3.11+ richiesto per tooling/test. `SQLGLOT_PYTHON` seleziona l'eseguibile per bridge e test; il venv oracle non è selezionato automaticamente per ogni comando.
- Il framework è **net10.0**; l'ambiente seleziona attualmente un **SDK 11 preview**. Questo non cambia il requisito .NET 10.

Comandi utili:

```powershell
# Tutti i casi di tokenizzazione, senza i livelli SQL non portati:
dotnet test src/SqlGlotSharp.CorpusTests --filter Level=L0

# Controllo dei membri Python / anchor C#:
dotnet run --project src/PortingTools -- src/tools/port_status.py --next 15 --file sqlglot/parser.py

# Rigenerazione principale, solo se necessaria:
dotnet run --project src/PortingTools -- src/tools/gen_csharp.py

# Preflight ambiente oracle, non l'intera registrazione:
./ops/run-oracle.ps1 -CheckOnly
```

Lo scoreboard viene scritto normalmente in `D:\Dev\BuildArtifacts\sqlglot-csharp\reports\scoreboard.md` con snapshot JSON e diagnostica dei fallimenti. Accetta un TRX completo come terzo argomento dopo corpus e directory report; rifiuta input incompleti/duplicati/sconosciuti/saltati.

## 5. Limiti e punti aperti da non dimenticare

- `DataType.from_str` funziona solo per UNKNOWN; le altre stringhe richiedono il parser ed esplicitamente generano NotPorted.
- Il ramo generale `Expression.to_py` per gli errori dipende dalla generazione SQL ed è uno stub; le conversioni Literal/Neg/Null/Boolean sono implementate.
- `ITypeAnnotator` ha metodi default che generano NotPorted: non è il motore di inferenza.
- I metodi/attributi non portati devono restare visibili a `port_status`; un'anchor aggiornata o una dichiarazione generata **non dimostrano comportamento completo**.
- Alcuni metadati/decoratori/alias Python restano da adattare; consultare l'inventario invece di dichiararli conclusi.
- Interi ordinari statically typed: Int64, con limiti espliciti. Indici JSONPath e valori JSON interi grandi in argomenti object: BigInteger. Usare `serde.dump_json` / `serde.JsonOptions` per conservarli esattamente.
- PyDecimal conserva valori letterali esatti; non implementa l'intero contesto/arithmetic Python Decimal.
- Deepcopy runtime non supporta ogni oggetto/collezione Python; set arbitrari e tuple con riferimenti richiedono attenzione. L'ordine dei set .NET non riproduce il layout hash CPython.
- Casing dipende dalle tabelle generate del Python oracle; rigenerarle se cambia Unicode/versione dell'oracolo.
- Tokenizer interno: indici in code point Python. Il verifier L0 converte offset/colonne in UTF-16; prestare attenzione allo stesso confine quando si implementa il verifier del parser L1.
- Correzioni recorder già fatte: identità del tokenizer concreto, snapshot immediato dei risultati mutabili, filtro dei tokenizer BigQuery fuori scope. Non dedurre il tokenizer dal contenuto SQL e non patchare i golden per far passare C#.
- Le rigenerazioni hanno un manifest dei file posseduti; non sovrascrivere manualmente `*.g.cs`. I generatori aggiuntivi hanno script distinti: non presumere che `gen_csharp.py` rigeneri tutti gli artefatti aggiunti successivamente.
- La suite Python completa può impiegare diverse ore. Il corpus è già disponibile: usare fixture/test mirati nello sviluppo e suite complessive ai checkpoint, non rigenerare l'oracolo senza motivo.
- Inizializzazione Git/gestione dei branch e packaging non sono stati completati da queste sessioni.

## 6. Ripresa: azioni concrete

1. Leggere questo documento, le regole e l'override del piano. Esplorare i file reali: non ricominciare dallo scaffold e non sovrascrivere il lavoro.
2. Rieseguire build e test principali. Se falliscono, riparare prima di continuare; confermare ogni fix.
3. Esaminare `parser.cs`, `parser.data.g.cs`, `core.functions.cs`, `dialect.builders.cs`, `typing/` e il report `port_status` per il parser.
4. Lavorare verso una **pipeline base completa**, con blocchi coerenti del parser e del generatore, mantenendo gli algoritmi Python e portando le dipendenze quando bloccano i test. Prima lacuna evidente: tabelle callable del parser (FUNCTIONS, STATEMENT_PARSERS, ecc.) e metodi di parsing. La sola struttura del cursore non analizza SQL.
5. Non perfezionare preventivamente tutto il runtime e non riprendere investimenti TSQL/Fabric. Preservarne i test già verdi come regressioni.
6. Quando il base è operativo, completare PostgreSQL; **TimescaleDB segue immediatamente**, con una specifica/test dedicati per la parte nuova.
7. Per la consegna finale servono parsing, generazione/transpiling, controlli di equivalenza, esempi eseguibili e istruzioni semplici. Non chiamare finale una demo tokenizer/AST.
8. Aggiornare questo snapshot solo dopo verifiche riuscite, distinguendo successi, fallimenti reali e NotPorted.

### Se la parallelizzazione di Ralf è pronta

- Verificare prima che l'ambiente la esponga realmente.
- Usare workspace/branch distinti: evitare due agenti sugli stessi file condivisi.
- Primo assetto suggerito: agente parser base/PostgreSQL; agente generatore base/PostgreSQL; coordinatore per contratti AST/runtime, integrazione e regressioni.
- Un eventuale terzo agente lavora su dipendenze ben delimitate e senza sovrapposizioni.
- Assegnare file, API condivise e criteri di test precisi; integrare a checkpoint con build/test complessivi.
- Non dichiarare avviati agenti o lavori in background se non sono stati effettivamente lanciati.
