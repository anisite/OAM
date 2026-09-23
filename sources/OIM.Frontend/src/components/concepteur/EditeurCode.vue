<template>
  <div ref="hote" class="editeur-code" :aria-label="libelle"></div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { EditorState } from '@codemirror/state'
import { EditorView, keymap, lineNumbers, highlightActiveLine } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { bracketMatching, defaultHighlightStyle, indentOnInput, syntaxHighlighting } from '@codemirror/language'
import { yaml } from '@codemirror/lang-yaml'

const props = withDefaults(defineProps<{ modelValue: string; libelle?: string; lectureSeule?: boolean; hauteur?: string }>(), {
  libelle: 'Éditeur YAML',
  hauteur: '480px'
})
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const hote = ref<HTMLDivElement | null>(null)
let vue: EditorView | null = null

onMounted(() => {
  vue = new EditorView({
    parent: hote.value!,
    state: EditorState.create({
      doc: props.modelValue,
      extensions: [
        lineNumbers(),
        history(),
        highlightActiveLine(),
        indentOnInput(),
        bracketMatching(),
        syntaxHighlighting(defaultHighlightStyle),
        yaml(),
        keymap.of([indentWithTab, ...defaultKeymap, ...historyKeymap]),
        EditorState.readOnly.of(props.lectureSeule),
        EditorView.theme({ '&': { height: props.hauteur }, '.cm-scroller': { overflow: 'auto' } }),
        EditorView.updateListener.of((u) => {
          if (u.docChanged) emit('update:modelValue', u.state.doc.toString())
        })
      ]
    })
  })
})

// Mise à jour externe (ex. régénération du YAML depuis le canevas)
watch(
  () => props.modelValue,
  (v) => {
    if (vue && v !== vue.state.doc.toString())
      vue.dispatch({ changes: { from: 0, to: vue.state.doc.length, insert: v } })
  }
)

onBeforeUnmount(() => vue?.destroy())
</script>
