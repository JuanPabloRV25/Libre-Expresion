# Portal Libre Expresión — reglas de desarrollo

- Mantén un solo monolito modular, conforme al ADR-004 de la bóveda oficial.
- Antes de implementar, consulta el índice, estado vigente, ADR y requisitos
  del alcance en la bóveda privada `Portal_Libre_Expresion/Modulos/`.
  En el workspace actual está en `../Portal_Libre_Expresion/Modulos/`.
  Si otro desarrollador no tiene la bóveda, debe obtener los documentos oficiales
  pertinentes antes de decidir cambios de arquitectura o reglas de negocio.
- `frontend/`: Vue y TypeScript; módulos funcionales en `src/features/`.
- `backend/`: API, Application, Domain e Infrastructure. Domain no depende de
  otras capas; Application depende de Domain; Infrastructure implementa contratos;
  API compone la aplicación. Respeta las adendas vigentes del ADR-004.
- `tests/`: pruebas automatizadas. `deploy/`: configuración y scripts operativos.
- Trabaja mediante ramas por tarea en este repositorio. No generes otra copia
  `.codex-*` ni paquetes por cada modificación.
- Si hace falta un worktree, registra finalidad, rama y ubicación bajo `Trabajo/`
  del workspace; integra o conserva sus cambios antes de cerrar la tarea.
- Conserva la documentación funcional, arquitectónica y operativa en Obsidian.
  El README del código contiene instrucciones técnicas breves y enlaces.
- Guarda evidencias necesarias en `Evidencias/<ticket>/` del workspace, entregas
  identificadas en `Entregas/<release>/` y temporales desechables fuera del proyecto.
- Valida los cambios con las pruebas pertinentes y documenta resultados y límites.
- No versiones secretos, originales privados ni datos operativos.
- Usa datos ficticios en pruebas: clientes, cotizaciones, direcciones e importes.
  Conserva las muestras reales únicamente fuera del repositorio.
- Mantén independientes DEV y PROD; una tarea local no autoriza publicar.
