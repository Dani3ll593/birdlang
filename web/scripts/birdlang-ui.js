(function () {
  'use strict';

  const htmlRoot = document.documentElement;
  const esIngles = htmlRoot.lang === 'en';
  const botonesTema = document.querySelectorAll('.btn-tema, #maqueta-btn-tema');

  function actualizarBotonesTema(esClaro) {
    botonesTema.forEach(function (btn) {
      if (esIngles) {
        btn.textContent = esClaro ? 'Dark mode' : 'Light mode';
      } else {
        btn.textContent = esClaro ? 'Modo oscuro' : 'Modo claro';
      }
    });
  }

  botonesTema.forEach(function (btn) {
    btn.addEventListener('click', function () {
      const esClaro = htmlRoot.getAttribute('data-tema') === 'claro';
      if (esClaro) {
        htmlRoot.removeAttribute('data-tema');
        actualizarBotonesTema(false);
      } else {
        htmlRoot.setAttribute('data-tema', 'claro');
        actualizarBotonesTema(true);
      }
    });
  });

  // Modo Supervisión en la maqueta (borde ámbar y apagado a los 20 min)
  const ventanaMaqueta = document.getElementById('ventana-maqueta');
  const btnSupervision = document.getElementById('maqueta-btn-supervision');
  const relojSupervision = document.getElementById('reloj-supervision');
  let idRelojSupervision = null;
  let segundosSupervision = 20 * 60;

  if (btnSupervision && ventanaMaqueta) {
    btnSupervision.addEventListener('click', function () {
      const activo = ventanaMaqueta.classList.toggle('modo-supervision');
      btnSupervision.classList.toggle('activo', activo);
      if (esIngles) {
        btnSupervision.textContent = activo ? 'Supervision on' : 'Hidden in capture';
      } else {
        btnSupervision.textContent = activo ? 'Supervisión activa' : 'Oculto en capturas';
      }

      if (activo) {
        segundosSupervision = 20 * 60;
        if (idRelojSupervision) clearInterval(idRelojSupervision);
        idRelojSupervision = setInterval(function () {
          segundosSupervision--;
          if (relojSupervision) {
            const m = Math.floor(segundosSupervision / 60);
            const s = segundosSupervision % 60;
            relojSupervision.textContent = m + ':' + (s < 10 ? '0' : '') + s;
          }
          if (segundosSupervision <= 0) {
            btnSupervision.click();
          }
        }, 1000);
      } else if (idRelojSupervision) {
        clearInterval(idRelojSupervision);
      }
    });
  }

  // Muestra local del glosario para la maqueta
  const muestraGlosario = [
    { en: 'aseguranza', es: 'insurance', pais: 'US', cat: esIngles ? 'Insurance' : 'Seguros' },
    { en: 'la troca', es: 'pickup truck', pais: 'US', cat: esIngles ? 'Vehicles' : 'Vehículos' },
    { en: 'los biles', es: 'utility bills', pais: 'US', cat: esIngles ? 'Finance' : 'Finanzas' },
    { en: 'deductible', es: 'deducible', pais: 'Todos', cat: esIngles ? 'Insurance' : 'Seguros' },
    { en: 'copay', es: 'copago', pais: 'Todos', cat: esIngles ? 'Medical' : 'Médico' },
    { en: 'guagua', es: 'bus', pais: 'PR', cat: 'General' },
    { en: 'guagua', es: 'bebé', pais: 'CO', cat: 'General' },
    { en: 'plazo fijo', es: 'certificate of deposit', pais: 'AR', cat: esIngles ? 'Finance' : 'Finanzas' },
    { en: 'chamba', es: 'job, work', pais: 'MX', cat: 'General' },
    { en: 'power of attorney', es: 'poder notarial', pais: 'Todos', cat: 'Legal' },
    { en: 'bail bond', es: 'fianza', pais: 'Todos', cat: 'Legal' },
    { en: 'chemotherapy', es: 'quimioterapia', pais: 'Todos', cat: esIngles ? 'Medical' : 'Médico' }
  ];

  const campoGlosario = document.getElementById('maqueta-campo-glosario');
  const selectorPais = document.getElementById('maqueta-selector-pais');
  const listaGlosario = document.getElementById('maqueta-lista-glosario');
  const areaNotas = document.getElementById('maqueta-notas');
  const btnBorrarNotas = document.getElementById('maqueta-borrar-notas');

  function insertarEnNotas(texto) {
    if (!areaNotas) return;
    // selectionStart vale 0 con el cursor al principio: no usar ||.
    const pos = typeof areaNotas.selectionStart === 'number' ? areaNotas.selectionStart : areaNotas.value.length;
    const actual = areaNotas.value;
    const sep = (pos > 0 && actual[pos - 1] !== ' ' && actual[pos - 1] !== '\n') ? ' ' : '';
    const sepDespues = (pos < actual.length && actual[pos] !== ' ' && actual[pos] !== '\n') ? ' ' : '';
    areaNotas.value = actual.slice(0, pos) + sep + texto + sepDespues + actual.slice(pos);
    const fin = pos + sep.length + texto.length + sepDespues.length;
    areaNotas.focus();
    areaNotas.setSelectionRange(fin, fin);
  }

  function pintarGlosario() {
    if (!listaGlosario) return;
    const q = campoGlosario ? campoGlosario.value.toLowerCase().trim() : '';
    const pais = selectorPais ? selectorPais.value : 'Todos';

    const filtrados = muestraGlosario.filter(function (item) {
      const okPais = (pais === 'Todos' || item.pais === pais || item.pais === 'Todos');
      const okTexto = !q || item.en.toLowerCase().includes(q) || item.es.toLowerCase().includes(q);
      return okPais && okTexto;
    });

    listaGlosario.innerHTML = '';
    filtrados.forEach(function (item) {
      const div = document.createElement('div');
      div.className = 'item-maqueta';
      div.innerHTML =
        '<div class="item-cabecera">' +
          '<span class="item-termino">' + item.en + '</span>' +
          '<span class="item-etiqueta">' + item.pais + '</span>' +
        '</div>' +
        '<div class="item-traduccion">' + item.es + '</div>' +
        '<div class="item-meta">' + item.cat + '</div>';
      div.addEventListener('click', function () {
        insertarEnNotas(item.es);
      });
      listaGlosario.appendChild(div);
    });
  }

  if (campoGlosario) campoGlosario.addEventListener('input', pintarGlosario);
  if (selectorPais) selectorPais.addEventListener('change', pintarGlosario);
  pintarGlosario();

  if (btnBorrarNotas && areaNotas) {
    btnBorrarNotas.addEventListener('click', function () {
      areaNotas.value = '';
      areaNotas.focus();
    });
  }

  // Pestañas de la columna izquierda (Glosario / Calles)
  const btnTabGlosario = document.getElementById('btn-tab-glosario');
  const btnTabCalles = document.getElementById('btn-tab-calles');
  const vistaGlosario = document.getElementById('vista-glosario');
  const vistaCalles = document.getElementById('vista-calles');

  function mostrarCalles(activarCalles) {
    if (!btnTabGlosario || !btnTabCalles || !vistaGlosario || !vistaCalles) return;
    btnTabGlosario.classList.toggle('activa', !activarCalles);
    btnTabCalles.classList.toggle('activa', activarCalles);
    vistaGlosario.style.display = activarCalles ? 'none' : 'flex';
    vistaCalles.style.display = activarCalles ? 'flex' : 'none';
  }

  if (btnTabGlosario) btnTabGlosario.addEventListener('click', function () { mostrarCalles(false); });
  if (btnTabCalles) btnTabCalles.addEventListener('click', function () { mostrarCalles(true); });

  // Ejemplos de calles y tipos de vía
  const campoCalles = document.getElementById('maqueta-campo-calles');
  const salidaCalle = document.getElementById('maqueta-salida-calle');
  const mapaCalles = {
    '1248 nort guálnut estrit': '1248 N Walnut St',
    'pichtri aveniu': 'Peachtree Ave',
    'bródgüey': 'Broadway',
    'jonigüel': 'Honeywell'
  };

  document.querySelectorAll('.btn-ejemplo-calle').forEach(function (btn) {
    btn.addEventListener('click', function () {
      const entrada = btn.getAttribute('data-calle');
      const resuelta = mapaCalles[entrada] || entrada;
      if (campoCalles) campoCalles.value = entrada;
      if (salidaCalle) {
        salidaCalle.textContent = resuelta;
      }
    });
  });

  // Lo que se escribe se compara con los ejemplos: la maqueta no trae el catálogo.
  function sinTildes(t) {
    return t.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').trim();
  }
  if (campoCalles && salidaCalle) {
    campoCalles.addEventListener('input', function () {
      const q = sinTildes(campoCalles.value);
      const clave = q && Object.keys(mapaCalles).find(function (k) {
        const n = sinTildes(k);
        return n.indexOf(q) === 0 || q.indexOf(n) === 0;
      });
      salidaCalle.textContent = clave ? mapaCalles[clave] : (esIngles ? 'Try one of the examples' : 'Prueba uno de los ejemplos');
    });
  }

  if (salidaCalle) {
    salidaCalle.parentElement.addEventListener('click', function () {
      // Solo una calle resuelta va a las notas, no el mensaje de ayuda.
      if (Object.values(mapaCalles).indexOf(salidaCalle.textContent) >= 0) {
        insertarEnNotas(salidaCalle.textContent);
      }
    });
  }

  document.querySelectorAll('.chip-via').forEach(function (chip) {
    chip.addEventListener('click', function () {
      insertarEnNotas(chip.textContent.trim());
    });
  });

  // Subtítulos: clic para pasar la frase a la otra banda
  document.querySelectorAll('.linea-sub').forEach(function (linea) {
    linea.addEventListener('click', function () {
      const bandaActual = linea.closest('.banda');
      const destino = bandaActual.classList.contains('banda-en')
        ? document.querySelector('.banda-es .banda-lineas')
        : document.querySelector('.banda-en .banda-lineas');
      if (destino) destino.appendChild(linea);
    });
  });

  // Copia de teléfono con vaciado a los 20 segundos
  const btnCopiarTel = document.getElementById('btn-copiar-tel');
  const btnLimpiarTel = document.getElementById('btn-limpiar-tel');
  const cajaChipTel = document.getElementById('caja-chip-tel');
  let idTimerTel = null;
  let segTel = 20;

  if (btnCopiarTel) {
    btnCopiarTel.addEventListener('click', function () {
      segTel = 20;
      btnCopiarTel.textContent = (esIngles ? 'Copied (' : 'Copiado (') + segTel + ' s)';
      if (idTimerTel) clearInterval(idTimerTel);
      idTimerTel = setInterval(function () {
        segTel--;
        if (segTel > 0) {
          btnCopiarTel.textContent = (esIngles ? 'Copied (' : 'Copiado (') + segTel + ' s)';
        } else {
          clearInterval(idTimerTel);
          btnCopiarTel.textContent = esIngles ? 'Copy (20 s)' : 'Copiar (20 s)';
        }
      }, 1000);
    });
  }

  if (btnLimpiarTel && cajaChipTel) {
    btnLimpiarTel.addEventListener('click', function () {
      if (idTimerTel) clearInterval(idTimerTel);
      cajaChipTel.textContent = esIngles ? 'Cleared' : 'Vacío';
    });
  }
})();
